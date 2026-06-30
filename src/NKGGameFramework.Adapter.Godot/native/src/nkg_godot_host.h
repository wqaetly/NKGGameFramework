#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

#include <godot_cpp/classes/node2d.hpp>
#include <godot_cpp/variant/string.hpp>

#include "nkg_godot_debug_transport.h"
#include "nkg_godot_host_command_reader.h"
#include "nkg_godot_node_registry.h"

namespace godot
{

class NkgGodotHost
{
public:
    using DebugHandler = NkgGodotDebugTransport::ManagedHandler;
    using FrameHandler = NkgGodotHostCommandReader::FrameHandler;
    using Node2DCommand = NkgGodotHostCommandReader::Node2DCommand;
    using NodeFactory = std::function<Node2D*(const Node2DCommand& p_command)>;
    using NodeHandler = std::function<void(const Node2DCommand& p_command, Node2D* p_node)>;

    ~NkgGodotHost();

    bool start_debug_transport(uint16_t p_port);
    void stop_debug_transport();
    bool is_debug_transport_running() const;
    uint16_t get_debug_port() const;
    std::string get_debug_last_error() const;
    void process_debug_transport(bool p_bridge_ready, const DebugHandler& p_handler);
    void publish_debug_stream_snapshots(bool p_bridge_ready, const DebugHandler& p_handler);

    bool apply_commands(
        const std::vector<uint8_t>& p_commands,
        const FrameHandler& p_frame_handler,
        const NodeFactory& p_node_factory,
        const NodeHandler& p_node_handler);

    size_t get_node_count() const;
    int32_t get_frame() const;

private:
    std::string make_node_key(const Node2DCommand& p_command) const;

    NkgGodotDebugTransport debug_transport;
    NkgGodotHostCommandReader command_reader;
    NkgGodotNodeRegistry nodes;
};

} // namespace godot
