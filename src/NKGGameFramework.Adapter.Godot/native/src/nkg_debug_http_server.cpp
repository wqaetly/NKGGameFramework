#include "nkg_debug_http_server.h"

#include <algorithm>
#include <cerrno>
#include <cctype>
#include <chrono>
#include <cstring>
#include <memory>
#include <sstream>
#include <thread>
#include <unordered_set>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
using nkg_socket_t = SOCKET;
constexpr nkg_socket_t NKG_INVALID_SOCKET = INVALID_SOCKET;
#else
#include <arpa/inet.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>
using nkg_socket_t = int;
constexpr nkg_socket_t NKG_INVALID_SOCKET = -1;
#endif

namespace
{
constexpr int MAX_HEADER_BYTES = 64 * 1024;
constexpr int MAX_BODY_BYTES = 128 * 1024 * 1024;
constexpr size_t MAX_STREAM_CLIENTS = 16;
constexpr const char* ENDPOINT_PREFIX = "/_nkg/debug";

#ifdef MSG_NOSIGNAL
constexpr int NKG_SEND_FLAGS = MSG_NOSIGNAL;
#else
constexpr int NKG_SEND_FLAGS = 0;
#endif

nkg_socket_t as_socket(uintptr_t value)
{
    return static_cast<nkg_socket_t>(value);
}

uintptr_t as_handle(nkg_socket_t value)
{
    return static_cast<uintptr_t>(value);
}

void close_socket(nkg_socket_t socket)
{
    if (socket == NKG_INVALID_SOCKET)
    {
        return;
    }

#ifdef _WIN32
    closesocket(socket);
#else
    close(socket);
#endif
}

int get_last_socket_error()
{
#ifdef _WIN32
    return WSAGetLastError();
#else
    return errno;
#endif
}

bool is_would_block_error(int error)
{
#ifdef _WIN32
    return error == WSAEWOULDBLOCK;
#else
    return error == EAGAIN || error == EWOULDBLOCK;
#endif
}

bool set_socket_non_blocking(nkg_socket_t socket)
{
#ifdef _WIN32
    u_long mode = 1;
    return ioctlsocket(socket, FIONBIO, &mode) == 0;
#else
    const int flags = fcntl(socket, F_GETFL, 0);
    return flags >= 0 && fcntl(socket, F_SETFL, flags | O_NONBLOCK) == 0;
#endif
}

bool send_all(nkg_socket_t socket, const std::string& data)
{
    const char* cursor = data.data();
    size_t remaining = data.size();
    while (remaining > 0)
    {
        const int chunk = remaining > 64 * 1024 ? 64 * 1024 : static_cast<int>(remaining);
        const int sent = send(socket, cursor, chunk, NKG_SEND_FLAGS);
        if (sent <= 0)
        {
            return false;
        }

        cursor += sent;
        remaining -= static_cast<size_t>(sent);
    }

    return true;
}

bool send_all_non_blocking(nkg_socket_t socket, const std::string& data)
{
    const char* cursor = data.data();
    size_t remaining = data.size();
    while (remaining > 0)
    {
        const int chunk = remaining > 64 * 1024 ? 64 * 1024 : static_cast<int>(remaining);
        const int sent = send(socket, cursor, chunk, NKG_SEND_FLAGS);
        if (sent <= 0)
        {
            return false;
        }

        cursor += sent;
        remaining -= static_cast<size_t>(sent);
    }

    return true;
}

bool stream_socket_is_alive(nkg_socket_t socket)
{
    char buffer;
    const int received = recv(socket, &buffer, 1, MSG_PEEK);
    if (received == 0)
    {
        return false;
    }

    if (received < 0)
    {
        return is_would_block_error(get_last_socket_error());
    }

    return true;
}

std::string to_lower(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

std::string trim(const std::string& value)
{
    const auto first = value.find_first_not_of(" \t\r\n");
    if (first == std::string::npos)
    {
        return std::string();
    }

    const auto last = value.find_last_not_of(" \t\r\n");
    return value.substr(first, last - first + 1);
}

std::string get_path(const std::string& target)
{
    const auto query = target.find('?');
    return query == std::string::npos ? target : target.substr(0, query);
}

bool target_has_query_key(const std::string& target, const std::string& key)
{
    return target.find("?" + key + "=") != std::string::npos || target.find("&" + key + "=") != std::string::npos;
}

void append_query_default(std::string& target, const std::string& key, const std::string& value)
{
    if (target_has_query_key(target, key))
    {
        return;
    }

    target += target.find('?') == std::string::npos ? '?' : '&';
    target += key;
    target += '=';
    target += value;
}

std::string make_snapshot_target(std::string target)
{
    const std::string stream_path = std::string(ENDPOINT_PREFIX) + "/stream";
    const std::string snapshot_path = std::string(ENDPOINT_PREFIX) + "/snapshot";
    if (target.rfind(stream_path, 0) == 0)
    {
        target.replace(0, stream_path.size(), snapshot_path);
    }

    append_query_default(target, "includePayload", "false");
    append_query_default(target, "includeStructured", "false");
    append_query_default(target, "waitForFrame", "false");
    return target;
}

std::string create_headers(const NkgDebugHttpServer::Response& response)
{
    std::ostringstream stream;
    stream << "HTTP/1.1 " << response.status_code << ' ' << response.reason << "\r\n"
           << "Content-Type: " << response.content_type << "\r\n"
           << "Content-Length: " << response.body.size() << "\r\n"
           << "Cache-Control: no-cache\r\n"
           << "Access-Control-Allow-Origin: *\r\n"
           << "Access-Control-Allow-Headers: content-type\r\n"
           << "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n"
           << "Connection: close\r\n\r\n";
    return stream.str();
}

bool read_http_request(
    nkg_socket_t socket,
    std::string& method,
    std::string& target,
    std::string& body,
    NkgDebugHttpServer::Response& error_response)
{
    std::string received;
    char buffer[4096];
    size_t header_end = std::string::npos;
    while (received.size() < MAX_HEADER_BYTES)
    {
        const int read = recv(socket, buffer, sizeof(buffer), 0);
        if (read <= 0)
        {
            return false;
        }

        received.append(buffer, static_cast<size_t>(read));
        header_end = received.find("\r\n\r\n");
        if (header_end != std::string::npos)
        {
            break;
        }
    }

    if (header_end == std::string::npos)
    {
        error_response = {400, "Bad Request", "application/json; charset=utf-8", "{\"message\":\"The debug request headers were too large.\"}"};
        return false;
    }

    const std::string header_text = received.substr(0, header_end);
    std::istringstream headers(header_text);
    std::string request_line;
    std::getline(headers, request_line);
    if (!request_line.empty() && request_line.back() == '\r')
    {
        request_line.pop_back();
    }

    std::istringstream request_line_stream(request_line);
    request_line_stream >> method >> target;
    if (method.empty() || target.empty())
    {
        error_response = {400, "Bad Request", "application/json; charset=utf-8", "{\"message\":\"The debug request line was malformed.\"}"};
        return false;
    }

    int content_length = 0;
    std::string line;
    while (std::getline(headers, line))
    {
        if (!line.empty() && line.back() == '\r')
        {
            line.pop_back();
        }

        const auto separator = line.find(':');
        if (separator == std::string::npos)
        {
            continue;
        }

        const auto name = to_lower(trim(line.substr(0, separator)));
        const auto value = trim(line.substr(separator + 1));
        if (name == "content-length")
        {
            content_length = std::atoi(value.c_str());
        }
    }

    if (content_length < 0 || content_length > MAX_BODY_BYTES)
    {
        error_response = {400, "Bad Request", "application/json; charset=utf-8", "{\"message\":\"The debug request body was too large.\"}"};
        return false;
    }

    body = received.substr(header_end + 4);
    while (body.size() < static_cast<size_t>(content_length))
    {
        const int read = recv(socket, buffer, sizeof(buffer), 0);
        if (read <= 0)
        {
            error_response = {400, "Bad Request", "application/json; charset=utf-8", "{\"message\":\"The HTTP request ended before the body completed.\"}"};
            return false;
        }

        body.append(buffer, static_cast<size_t>(read));
    }

    if (body.size() > static_cast<size_t>(content_length))
    {
        body.resize(static_cast<size_t>(content_length));
    }

    return true;
}
} // namespace

struct NkgDebugHttpServer::RequestState
{
    std::mutex mutex;
    std::condition_variable completed_cv;
    bool completed = false;
    Response response;
};

struct NkgDebugHttpServer::StreamClient
{
    uintptr_t socket = 0;
    std::string snapshot_target;
};

NkgDebugHttpServer::NkgDebugHttpServer()
    : running(false),
      listen_socket(as_handle(NKG_INVALID_SOCKET)),
      port(0),
      accept_thread(nullptr),
      next_request_id(1)
{
}

NkgDebugHttpServer::~NkgDebugHttpServer()
{
    stop();
}

bool NkgDebugHttpServer::start(uint16_t p_port)
{
    if (running.load())
    {
        return true;
    }

#ifdef _WIN32
    WSADATA wsa_data;
    if (WSAStartup(MAKEWORD(2, 2), &wsa_data) != 0)
    {
        set_error("WSAStartup failed.");
        return false;
    }
#endif

    nkg_socket_t socket = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (socket == NKG_INVALID_SOCKET)
    {
        set_error("Failed to create debug server socket.");
        return false;
    }

    int reuse = 1;
    setsockopt(socket, SOL_SOCKET, SO_REUSEADDR, reinterpret_cast<const char*>(&reuse), sizeof(reuse));

    sockaddr_in address {};
    address.sin_family = AF_INET;
    address.sin_port = htons(p_port);
    inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);

    if (bind(socket, reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0)
    {
        close_socket(socket);
        set_error("Failed to bind debug server to 127.0.0.1.");
        return false;
    }

    if (listen(socket, 16) != 0)
    {
        close_socket(socket);
        set_error("Failed to listen on debug server socket.");
        return false;
    }

    sockaddr_in bound_address {};
    socklen_t bound_size = sizeof(bound_address);
    if (getsockname(socket, reinterpret_cast<sockaddr*>(&bound_address), &bound_size) == 0)
    {
        port = ntohs(bound_address.sin_port);
    }
    else
    {
        port = p_port;
    }

    listen_socket = as_handle(socket);
    running.store(true);
    accept_thread = new std::thread([this]() {
        accept_loop();
    });
    return true;
}

void NkgDebugHttpServer::stop()
{
    if (!running.exchange(false))
    {
        return;
    }

    close_socket(as_socket(listen_socket));
    listen_socket = as_handle(NKG_INVALID_SOCKET);
    if (accept_thread != nullptr)
    {
        if (accept_thread->joinable())
        {
            accept_thread->join();
        }
        delete accept_thread;
        accept_thread = nullptr;
    }

    {
        std::lock_guard<std::mutex> lock(pending_mutex);
        for (auto& item : pending_states)
        {
            auto& state = item.second;
            {
                std::lock_guard<std::mutex> state_lock(state->mutex);
                state->response = {503, "Service Unavailable", "application/json; charset=utf-8", "{\"message\":\"Debug server stopped.\"}"};
                state->completed = true;
            }
            state->completed_cv.notify_one();
        }
        pending_states.clear();
    }

    close_all_stream_clients();

#ifdef _WIN32
    WSACleanup();
#endif
}

bool NkgDebugHttpServer::is_running() const
{
    return running.load();
}

uint16_t NkgDebugHttpServer::get_port() const
{
    return port;
}

std::string NkgDebugHttpServer::get_last_error() const
{
    std::lock_guard<std::mutex> lock(error_mutex);
    return last_error;
}

bool NkgDebugHttpServer::pop_pending_request(PendingRequest& request)
{
    std::lock_guard<std::mutex> lock(pending_mutex);
    if (pending_requests.empty())
    {
        return false;
    }

    request = pending_requests.front();
    pending_requests.pop();
    return true;
}

void NkgDebugHttpServer::complete_request(uint64_t id, const Response& response)
{
    std::shared_ptr<RequestState> state;
    {
        std::lock_guard<std::mutex> lock(pending_mutex);
        auto found = pending_states.find(id);
        if (found == pending_states.end())
        {
            return;
        }

        state = found->second;
        pending_states.erase(found);
    }

    {
        std::lock_guard<std::mutex> state_lock(state->mutex);
        state->response = response;
        state->completed = true;
    }
    state->completed_cv.notify_one();
}

std::vector<std::string> NkgDebugHttpServer::get_stream_snapshot_targets()
{
    std::vector<std::string> targets;
    std::unordered_set<std::string> unique;
    std::lock_guard<std::mutex> lock(stream_mutex);
    prune_closed_stream_clients_locked();
    for (const auto& client : stream_clients)
    {
        if (unique.insert(client.snapshot_target).second)
        {
            targets.push_back(client.snapshot_target);
        }
    }

    return targets;
}

void NkgDebugHttpServer::broadcast_snapshot(const std::string& snapshot_target, const std::string& json_body)
{
    const std::string event = "event: snapshot\n" + std::string("data: ") + json_body + "\n\n";
    std::lock_guard<std::mutex> lock(stream_mutex);
    prune_closed_stream_clients_locked();
    auto write = stream_clients.begin();
    for (auto read = stream_clients.begin(); read != stream_clients.end(); ++read)
    {
        bool keep = true;
        if (read->snapshot_target == snapshot_target)
        {
            keep = send_all_non_blocking(as_socket(read->socket), event);
        }

        if (keep)
        {
            *write++ = *read;
        }
        else
        {
            close_socket(as_socket(read->socket));
        }
    }

    stream_clients.erase(write, stream_clients.end());
}

NkgDebugHttpServer::Response NkgDebugHttpServer::parse_managed_response(const std::string& value)
{
    const auto first = value.find('\n');
    const auto second = first == std::string::npos ? std::string::npos : value.find('\n', first + 1);
    const auto third = second == std::string::npos ? std::string::npos : value.find('\n', second + 1);
    if (first == std::string::npos || second == std::string::npos || third == std::string::npos)
    {
        return {500, "Internal Server Error", "application/json; charset=utf-8", "{\"message\":\"Managed debug response was malformed.\"}"};
    }

    Response response;
    response.status_code = std::atoi(value.substr(0, first).c_str());
    response.reason = value.substr(first + 1, second - first - 1);
    response.content_type = value.substr(second + 1, third - second - 1);
    response.body = value.substr(third + 1);
    if (response.status_code <= 0)
    {
        response.status_code = 500;
    }
    if (response.reason.empty())
    {
        response.reason = "OK";
    }
    if (response.content_type.empty())
    {
        response.content_type = "application/json; charset=utf-8";
    }

    return response;
}

void NkgDebugHttpServer::accept_loop()
{
    while (running.load())
    {
        sockaddr_in client_address {};
        socklen_t client_size = sizeof(client_address);
        nkg_socket_t client = accept(as_socket(listen_socket), reinterpret_cast<sockaddr*>(&client_address), &client_size);
        if (client == NKG_INVALID_SOCKET)
        {
            if (running.load())
            {
                std::this_thread::sleep_for(std::chrono::milliseconds(5));
            }
            continue;
        }

        handle_client(as_handle(client));
    }
}

void NkgDebugHttpServer::handle_client(uintptr_t client_handle)
{
    nkg_socket_t client = as_socket(client_handle);
    Response response;
    std::string method;
    std::string target;
    std::string body;
    if (!read_http_request(client, method, target, body, response))
    {
        send_all(client, create_headers(response) + response.body);
        close_socket(client);
        return;
    }

    if (to_lower(method) == "options")
    {
        response = {204, "No Content", "text/plain; charset=utf-8", ""};
        send_all(client, create_headers(response));
        close_socket(client);
        return;
    }

    if (to_lower(method) == "get" && get_path(target) == std::string(ENDPOINT_PREFIX) + "/stream")
    {
        const std::string headers =
            "HTTP/1.1 200 OK\r\n"
            "Content-Type: text/event-stream\r\n"
            "Cache-Control: no-cache\r\n"
            "Connection: keep-alive\r\n"
            "Access-Control-Allow-Origin: *\r\n"
            "Access-Control-Allow-Headers: content-type\r\n"
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n\r\n";
        if (send_all(client, headers))
        {
            add_stream_client(client_handle, make_snapshot_target(target));
            return;
        }

        close_socket(client);
        return;
    }

    if (!enqueue_request(method, target, body, response))
    {
        send_all(client, create_headers(response) + response.body);
        close_socket(client);
        return;
    }

    send_all(client, create_headers(response) + response.body);
    close_socket(client);
}

bool NkgDebugHttpServer::enqueue_request(const std::string& method, const std::string& target, const std::string& body, Response& response)
{
    auto state = std::make_shared<RequestState>();
    uint64_t id = 0;
    {
        std::lock_guard<std::mutex> lock(pending_mutex);
        id = next_request_id++;
        pending_states[id] = state;
        pending_requests.push(PendingRequest{id, method, target, body});
    }

    std::unique_lock<std::mutex> state_lock(state->mutex);
    state->completed_cv.wait(state_lock, [&]() {
        return state->completed || !running.load();
    });
    response = state->response;
    return state->completed;
}

void NkgDebugHttpServer::add_stream_client(uintptr_t client_socket, const std::string& target)
{
    const nkg_socket_t socket = as_socket(client_socket);
    if (!set_socket_non_blocking(socket))
    {
        close_socket(socket);
        return;
    }

    std::lock_guard<std::mutex> lock(stream_mutex);
    prune_closed_stream_clients_locked();
    stream_clients.push_back(StreamClient{client_socket, target});
    while (stream_clients.size() > MAX_STREAM_CLIENTS)
    {
        close_socket(as_socket(stream_clients.front().socket));
        stream_clients.erase(stream_clients.begin());
    }
}

void NkgDebugHttpServer::prune_closed_stream_clients_locked()
{
    auto write = stream_clients.begin();
    for (auto read = stream_clients.begin(); read != stream_clients.end(); ++read)
    {
        if (stream_socket_is_alive(as_socket(read->socket)))
        {
            *write++ = *read;
        }
        else
        {
            close_socket(as_socket(read->socket));
        }
    }

    stream_clients.erase(write, stream_clients.end());
}

void NkgDebugHttpServer::close_all_stream_clients()
{
    std::lock_guard<std::mutex> lock(stream_mutex);
    for (const auto& client : stream_clients)
    {
        close_socket(as_socket(client.socket));
    }
    stream_clients.clear();
}

void NkgDebugHttpServer::set_error(const std::string& message)
{
    std::lock_guard<std::mutex> lock(error_mutex);
    last_error = message;
}
