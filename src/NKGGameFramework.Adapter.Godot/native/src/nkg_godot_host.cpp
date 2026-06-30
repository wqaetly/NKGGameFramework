#include "nkg_godot_host.h"

#include <godot_cpp/classes/canvas_item.hpp>
#include <godot_cpp/classes/label.hpp>
#include <godot_cpp/classes/polygon2d.hpp>
#include <godot_cpp/core/memory.hpp>
#include <godot_cpp/variant/color.hpp>
#include <godot_cpp/variant/packed_vector2_array.hpp>
#include <godot_cpp/variant/string.hpp>
#include <godot_cpp/variant/vector2.hpp>

namespace godot
{

NkgGodotHost::~NkgGodotHost()
{
    stop_debug_transport();
}

void NkgGodotHost::set_root(Node* p_root)
{
    root = p_root;
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
    objects.begin_frame();
    bool applied = true;

    NkgGodotHostCommandReader::Handlers handlers;
    handlers.frame = p_frame_handler;
    handlers.node2d = [this, &p_node_factory, &p_node_handler](const Node2DCommand& command) {
            Node2D* node = objects.sync_node2d(make_node_key(command), [&p_node_factory, &command]() {
                return p_node_factory(command);
            });
            if (node != nullptr)
            {
                p_node_handler(command, node);
            }
        };
    handlers.create_node = [this, &applied](const NkgGodotHostCommandReader::CreateNodeCommand& command) {
        applied = apply_create_node(command) && applied;
    };
    handlers.destroy_object = [this, &applied](const NkgGodotHostCommandReader::DestroyObjectCommand& command) {
        applied = apply_destroy_object(command) && applied;
    };
    handlers.set_parent = [this, &applied](const NkgGodotHostCommandReader::SetParentCommand& command) {
        applied = apply_set_parent(command) && applied;
    };
    handlers.set_transform2d = [this, &applied](const NkgGodotHostCommandReader::SetTransform2DCommand& command) {
        applied = apply_set_transform2d(command) && applied;
    };
    handlers.set_visible = [this, &applied](const NkgGodotHostCommandReader::SetVisibleCommand& command) {
        applied = apply_set_visible(command) && applied;
    };
    handlers.set_property = [this, &applied](const NkgGodotHostCommandReader::SetPropertyCommand& command) {
        applied = apply_set_property(command) && applied;
    };

    const bool read = command_reader.read(p_commands, handlers);
    if (!read || !applied)
    {
        return false;
    }

    objects.remove_stale_objects();
    return true;
}

size_t NkgGodotHost::get_node_count() const
{
    return objects.size();
}

int32_t NkgGodotHost::get_frame() const
{
    return objects.frame();
}

std::string NkgGodotHost::make_node_key(const Node2DCommand& p_command) const
{
    return p_command.kind + ":" + std::to_string(p_command.id);
}

std::string NkgGodotHost::make_object_key(int32_t p_id) const
{
    return "object:" + std::to_string(p_id);
}

bool NkgGodotHost::apply_create_node(const NkgGodotHostCommandReader::CreateNodeCommand& p_command)
{
    Object* object = objects.sync_object(
        make_object_key(p_command.id),
        [this, &p_command]() {
            return create_object_by_type(p_command.type_name);
        },
        [this](Object* object) {
            release_object(object);
        });
    if (object == nullptr)
    {
        return false;
    }

    Node* node = Object::cast_to<Node>(object);
    if (node == nullptr)
    {
        return false;
    }

    if (!p_command.name.empty())
    {
        node->set_name(String(p_command.name.c_str()));
    }

    if (root != nullptr && node->get_parent() == nullptr)
    {
        root->add_child(node);
    }

    return true;
}

bool NkgGodotHost::apply_destroy_object(const NkgGodotHostCommandReader::DestroyObjectCommand& p_command)
{
    return objects.release_object(make_object_key(p_command.id));
}

bool NkgGodotHost::apply_set_parent(const NkgGodotHostCommandReader::SetParentCommand& p_command)
{
    Node* child = Object::cast_to<Node>(objects.get_object(make_object_key(p_command.child_id)));
    Node* parent = p_command.parent_id == 0
        ? root
        : Object::cast_to<Node>(objects.get_object(make_object_key(p_command.parent_id)));
    if (child == nullptr || parent == nullptr)
    {
        return false;
    }

    Node* current_parent = child->get_parent();
    if (current_parent == parent)
    {
        return true;
    }

    if (current_parent != nullptr)
    {
        current_parent->remove_child(child);
    }
    parent->add_child(child);
    return true;
}

bool NkgGodotHost::apply_set_transform2d(const NkgGodotHostCommandReader::SetTransform2DCommand& p_command)
{
    Node2D* node = Object::cast_to<Node2D>(objects.get_object(make_object_key(p_command.id)));
    if (node == nullptr)
    {
        return false;
    }

    node->set_position(Vector2(p_command.x, p_command.y));
    node->set_rotation(p_command.rotation);
    node->set_scale(Vector2(p_command.scale_x, p_command.scale_y));
    return true;
}

bool NkgGodotHost::apply_set_visible(const NkgGodotHostCommandReader::SetVisibleCommand& p_command)
{
    CanvasItem* item = Object::cast_to<CanvasItem>(objects.get_object(make_object_key(p_command.id)));
    if (item == nullptr)
    {
        return false;
    }

    item->set_visible(p_command.visible);
    return true;
}

bool NkgGodotHost::apply_set_property(const NkgGodotHostCommandReader::SetPropertyCommand& p_command)
{
    Object* object = objects.get_object(make_object_key(p_command.id));
    Polygon2D* polygon = Object::cast_to<Polygon2D>(object);
    if (polygon != nullptr &&
        p_command.property_name == "color" &&
        p_command.value.kind == NkgGodotHostCommandReader::VariantKind::Color)
    {
        polygon->set_color(Color(
            p_command.value.r,
            p_command.value.g,
            p_command.value.b,
            p_command.value.a));
        return true;
    }

    if (polygon != nullptr &&
        p_command.property_name == "polygon" &&
        p_command.value.kind == NkgGodotHostCommandReader::VariantKind::PackedVector2Array)
    {
        PackedVector2Array points;
        for (const auto& point : p_command.value.vector2_array)
        {
            points.push_back(Vector2(point.x, point.y));
        }
        polygon->set_polygon(points);
        return true;
    }

    Label* label = Object::cast_to<Label>(object);
    if (label != nullptr &&
        p_command.property_name == "text" &&
        p_command.value.kind == NkgGodotHostCommandReader::VariantKind::String)
    {
        label->set_text(String(p_command.value.text.c_str()));
        return true;
    }

    return false;
}

Object* NkgGodotHost::create_object_by_type(const std::string& p_type_name) const
{
    if (p_type_name == "Node2D")
    {
        return memnew(Node2D);
    }

    if (p_type_name == "Polygon2D")
    {
        return memnew(Polygon2D);
    }

    if (p_type_name == "Label")
    {
        return memnew(Label);
    }

    return nullptr;
}

void NkgGodotHost::release_object(Object* p_object) const
{
    Node* node = Object::cast_to<Node>(p_object);
    if (node != nullptr)
    {
        node->queue_free();
        return;
    }

    memdelete(p_object);
}

} // namespace godot
