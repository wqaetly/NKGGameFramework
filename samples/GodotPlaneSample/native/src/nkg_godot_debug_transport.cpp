#include "nkg_godot_debug_transport.h"

#include <string>

namespace
{
std::string to_std_string(const godot::String& value)
{
    const auto utf8 = value.utf8();
    return std::string(utf8.get_data(), utf8.length());
}

std::string base64_encode(const std::string& value)
{
    static constexpr char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string encoded;
    encoded.reserve(((value.size() + 2) / 3) * 4);

    size_t index = 0;
    while (index + 3 <= value.size())
    {
        const uint32_t block =
            (static_cast<uint32_t>(static_cast<unsigned char>(value[index])) << 16) |
            (static_cast<uint32_t>(static_cast<unsigned char>(value[index + 1])) << 8) |
            static_cast<uint32_t>(static_cast<unsigned char>(value[index + 2]));
        encoded.push_back(alphabet[(block >> 18) & 0x3f]);
        encoded.push_back(alphabet[(block >> 12) & 0x3f]);
        encoded.push_back(alphabet[(block >> 6) & 0x3f]);
        encoded.push_back(alphabet[block & 0x3f]);
        index += 3;
    }

    const size_t remaining = value.size() - index;
    if (remaining == 1)
    {
        const uint32_t block = static_cast<uint32_t>(static_cast<unsigned char>(value[index])) << 16;
        encoded.push_back(alphabet[(block >> 18) & 0x3f]);
        encoded.push_back(alphabet[(block >> 12) & 0x3f]);
        encoded.push_back('=');
        encoded.push_back('=');
    }
    else if (remaining == 2)
    {
        const uint32_t block =
            (static_cast<uint32_t>(static_cast<unsigned char>(value[index])) << 16) |
            (static_cast<uint32_t>(static_cast<unsigned char>(value[index + 1])) << 8);
        encoded.push_back(alphabet[(block >> 18) & 0x3f]);
        encoded.push_back(alphabet[(block >> 12) & 0x3f]);
        encoded.push_back(alphabet[(block >> 6) & 0x3f]);
        encoded.push_back('=');
    }

    return encoded;
}

godot::String make_managed_debug_request(
    const std::string& method,
    const std::string& target,
    const std::string& body)
{
    const std::string payload = method + "\n" + target + "\nbase64\n" + base64_encode(body);
    return godot::String::utf8(payload.c_str(), payload.size());
}

NkgDebugHttpServer::Response bridge_not_ready_response()
{
    return NkgDebugHttpServer::Response{
        503,
        "Service Unavailable",
        "application/json; charset=utf-8",
        "{\"message\":\"LeanCLR bridge is not ready.\"}"};
}
} // namespace

namespace godot
{

bool NkgGodotDebugTransport::start(uint16_t p_port)
{
    return server.start(p_port);
}

void NkgGodotDebugTransport::stop()
{
    server.stop();
}

bool NkgGodotDebugTransport::is_running() const
{
    return server.is_running();
}

uint16_t NkgGodotDebugTransport::get_port() const
{
    return server.get_port();
}

std::string NkgGodotDebugTransport::get_last_error() const
{
    return server.get_last_error();
}

void NkgGodotDebugTransport::process_pending_requests(bool p_bridge_ready, const ManagedHandler& p_handler)
{
    if (!server.is_running())
    {
        return;
    }

    NkgDebugHttpServer::PendingRequest request;
    int32_t guard = 0;
    while (guard++ < 32 && server.pop_pending_request(request))
    {
        if (!p_bridge_ready)
        {
            server.complete_request(request.id, bridge_not_ready_response());
            continue;
        }

        const String managed_response = p_handler(
            make_managed_debug_request(request.method, request.target, request.body));
        server.complete_request(
            request.id,
            NkgDebugHttpServer::parse_managed_response(to_std_string(managed_response)));
    }
}

void NkgGodotDebugTransport::publish_stream_snapshots(bool p_bridge_ready, const ManagedHandler& p_handler)
{
    if (!server.is_running() || !p_bridge_ready)
    {
        return;
    }

    const auto targets = server.get_stream_snapshot_targets();
    for (const auto& target : targets)
    {
        const String managed_response = p_handler(make_managed_debug_request("GET", target, ""));
        const auto response = NkgDebugHttpServer::parse_managed_response(to_std_string(managed_response));
        if (response.status_code == 200)
        {
            server.broadcast_snapshot(target, response.body);
        }
    }
}

} // namespace godot
