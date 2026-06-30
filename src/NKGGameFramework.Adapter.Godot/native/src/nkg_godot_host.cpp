#include "nkg_godot_host.h"

namespace godot
{

NkgGodotHost::~NkgGodotHost()
{
    stop_debug_transport();
}

bool NkgGodotHost::start_debug_transport(uint16_t p_port)
{
    return debug_transport.start(p_port);
}

void NkgGodotHost::stop_debug_transport()
{
    debug_transport.stop();
}

bool NkgGodotHost::is_debug_transport_running() const
{
    return debug_transport.is_running();
}

uint16_t NkgGodotHost::get_debug_port() const
{
    return debug_transport.get_port();
}

std::string NkgGodotHost::get_debug_last_error() const
{
    return debug_transport.get_last_error();
}

void NkgGodotHost::process_debug_transport(bool p_bridge_ready, const DebugHandler& p_handler)
{
    debug_transport.process_pending_requests(p_bridge_ready, p_handler);
}

void NkgGodotHost::publish_debug_stream_snapshots(bool p_bridge_ready, const DebugHandler& p_handler)
{
    debug_transport.publish_stream_snapshots(p_bridge_ready, p_handler);
}

bool NkgGodotHost::apply_commands(
    const std::vector<uint8_t>& p_commands,
    const FrameHandler& p_frame_handler,
    const NodeFactory& p_node_factory,
    const NodeHandler& p_node_handler)
{
    nodes.begin_frame();
    const bool read = command_reader.read(
        p_commands,
        p_frame_handler,
        [this, &p_node_factory, &p_node_handler](const Node2DCommand& command) {
            Node2D* node = nodes.sync_node(make_node_key(command), [&p_node_factory, &command]() {
                return p_node_factory(command);
            });
            if (node != nullptr)
            {
                p_node_handler(command, node);
            }
        });

    if (!read)
    {
        return false;
    }

    nodes.remove_stale_nodes();
    return true;
}

size_t NkgGodotHost::get_node_count() const
{
    return nodes.size();
}

int32_t NkgGodotHost::get_frame() const
{
    return nodes.frame();
}

std::string NkgGodotHost::make_node_key(const Node2DCommand& p_command) const
{
    return p_command.kind + ":" + std::to_string(p_command.id);
}

} // namespace godot
