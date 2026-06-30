#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

#include <godot_cpp/classes/node.hpp>
#include <godot_cpp/classes/node2d.hpp>
#include <godot_cpp/core/object.hpp>
#include <godot_cpp/variant/string.hpp>
#include <godot_cpp/variant/variant.hpp>

#include "nkg_godot_debug_transport.h"
#include "nkg_godot_host_command_reader.h"
#include "nkg_godot_object_registry.h"
#include "nkg_godot_resource_registry.h"

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

    void set_root(Node* p_root);
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
    std::string make_object_key(int32_t p_id) const;
    bool apply_create_node(const NkgGodotHostCommandReader::CreateNodeCommand& p_command);
    bool apply_destroy_object(const NkgGodotHostCommandReader::DestroyObjectCommand& p_command);
    bool apply_set_parent(const NkgGodotHostCommandReader::SetParentCommand& p_command);
    bool apply_set_transform2d(const NkgGodotHostCommandReader::SetTransform2DCommand& p_command);
    bool apply_set_visible(const NkgGodotHostCommandReader::SetVisibleCommand& p_command);
    bool apply_set_property(const NkgGodotHostCommandReader::SetPropertyCommand& p_command);
    bool apply_call_method(const NkgGodotHostCommandReader::CallMethodCommand& p_command);
    bool apply_load_resource(const NkgGodotHostCommandReader::LoadResourceCommand& p_command);
    bool apply_release_resource(const NkgGodotHostCommandReader::ReleaseResourceCommand& p_command);
    bool try_convert_variant(const NkgGodotHostCommandReader::VariantValue& p_value, Variant& p_variant) const;
    Object* create_object_by_type(const std::string& p_type_name) const;
    void release_object(Object* p_object) const;

    Node* root = nullptr;
    NkgGodotDebugTransport debug_transport;
    NkgGodotHostCommandReader command_reader;
    NkgGodotObjectRegistry objects;
    NkgGodotResourceRegistry resources;
};

} // namespace godot
