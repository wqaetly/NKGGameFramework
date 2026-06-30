#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

#include <godot_cpp/variant/string.hpp>

namespace godot
{

class NkgGodotHostCommandReader
{
public:
    struct FrameCommand
    {
        int32_t frame = 0;
        int32_t primary_value = 0;
        int32_t secondary_value = 0;
        bool terminal = false;
    };

    struct Node2DCommand
    {
        std::string kind;
        int32_t id = 0;
        double x = 0;
        double y = 0;
    };

    struct CreateNodeCommand
    {
        int32_t id = 0;
        std::string type_name;
        std::string name;
    };

    struct DestroyObjectCommand
    {
        int32_t id = 0;
    };

    struct SetParentCommand
    {
        int32_t child_id = 0;
        int32_t parent_id = 0;
    };

    struct SetTransform2DCommand
    {
        int32_t id = 0;
        double x = 0;
        double y = 0;
        double rotation = 0;
        double scale_x = 1;
        double scale_y = 1;
    };

    struct SetVisibleCommand
    {
        int32_t id = 0;
        bool visible = true;
    };

    enum class VariantKind : uint8_t
    {
        Color = 1,
        PackedVector2Array = 2
    };

    struct Vector2Value
    {
        double x = 0;
        double y = 0;
    };

    struct VariantValue
    {
        VariantKind kind = VariantKind::Color;
        double r = 0;
        double g = 0;
        double b = 0;
        double a = 1;
        std::vector<Vector2Value> vector2_array;
    };

    struct SetPropertyCommand
    {
        int32_t id = 0;
        std::string property_name;
        VariantValue value;
    };

    using FrameHandler = std::function<void(const FrameCommand& p_command)>;
    using Node2DHandler = std::function<void(const Node2DCommand& p_command)>;
    using CreateNodeHandler = std::function<void(const CreateNodeCommand& p_command)>;
    using DestroyObjectHandler = std::function<void(const DestroyObjectCommand& p_command)>;
    using SetParentHandler = std::function<void(const SetParentCommand& p_command)>;
    using SetTransform2DHandler = std::function<void(const SetTransform2DCommand& p_command)>;
    using SetVisibleHandler = std::function<void(const SetVisibleCommand& p_command)>;
    using SetPropertyHandler = std::function<void(const SetPropertyCommand& p_command)>;

    struct Handlers
    {
        FrameHandler frame;
        Node2DHandler node2d;
        CreateNodeHandler create_node;
        DestroyObjectHandler destroy_object;
        SetParentHandler set_parent;
        SetTransform2DHandler set_transform2d;
        SetVisibleHandler set_visible;
        SetPropertyHandler set_property;
    };

    bool read(const std::vector<uint8_t>& p_commands, const Handlers& p_handlers) const;
    bool read(const String& p_commands, const Handlers& p_handlers) const;
    bool read(const std::vector<uint8_t>& p_commands, const FrameHandler& p_frame_handler, const Node2DHandler& p_node_handler) const;
    bool read(const String& p_commands, const FrameHandler& p_frame_handler, const Node2DHandler& p_node_handler) const;
};

} // namespace godot
