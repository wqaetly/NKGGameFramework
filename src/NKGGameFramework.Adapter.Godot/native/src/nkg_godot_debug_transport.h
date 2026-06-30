#pragma once

#include <cstdint>
#include <functional>
#include <string>

#include <godot_cpp/variant/string.hpp>

#include "nkg_debug_http_server.h"

namespace godot
{

class NkgGodotDebugTransport
{
public:
    using ManagedHandler = std::function<String(const String& p_request)>;

    bool start(uint16_t p_port);
    void stop();
    bool is_running() const;
    uint16_t get_port() const;
    std::string get_last_error() const;

    void process_pending_requests(bool p_bridge_ready, const ManagedHandler& p_handler);
    void publish_stream_snapshots(bool p_bridge_ready, const ManagedHandler& p_handler);

private:
    NkgDebugHttpServer server;
};

} // namespace godot
