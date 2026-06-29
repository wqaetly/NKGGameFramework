#pragma once

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <mutex>
#include <queue>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

class NkgDebugHttpServer
{
public:
    struct PendingRequest
    {
        uint64_t id = 0;
        std::string method;
        std::string target;
        std::string body;
    };

    struct Response
    {
        int status_code = 500;
        std::string reason = "Internal Server Error";
        std::string content_type = "application/json; charset=utf-8";
        std::string body = "{\"message\":\"Native debug bridge failed.\"}";
    };

    NkgDebugHttpServer();
    ~NkgDebugHttpServer();

    bool start(uint16_t port);
    void stop();
    bool is_running() const;
    uint16_t get_port() const;
    std::string get_last_error() const;

    bool pop_pending_request(PendingRequest& request);
    void complete_request(uint64_t id, const Response& response);

    std::vector<std::string> get_stream_snapshot_targets() const;
    void broadcast_snapshot(const std::string& snapshot_target, const std::string& json_body);

    static Response parse_managed_response(const std::string& value);

private:
    struct RequestState;
    struct StreamClient;

    void accept_loop();
    void handle_client(uintptr_t client_socket);
    bool enqueue_request(const std::string& method, const std::string& target, const std::string& body, Response& response);
    void add_stream_client(uintptr_t client_socket, const std::string& target);
    void close_all_stream_clients();
    void set_error(const std::string& message);

    std::atomic<bool> running;
    uintptr_t listen_socket;
    uint16_t port;
    mutable std::mutex error_mutex;
    std::string last_error;
    std::thread* accept_thread;

    mutable std::mutex pending_mutex;
    std::queue<PendingRequest> pending_requests;
    std::unordered_map<uint64_t, std::shared_ptr<RequestState>> pending_states;
    uint64_t next_request_id;

    mutable std::mutex stream_mutex;
    std::vector<StreamClient> stream_clients;
};
