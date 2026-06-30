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
        PackedVector2Array = 2,
        String = 3,
        Bool = 4,
        Integer = 5,
        Float = 6,
        Vector2 = 7,
        Resource = 8
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
        bool boolean = false;
        int64_t integer = 0;
        double number = 0;
        Vector2Value vector2;
        int32_t resource_id = 0;
        std::string text;
        std::vector<Vector2Value> vector2_array;
    };

    struct SetPropertyCommand
    {
        int32_t id = 0;
        std::string property_name;
        VariantValue value;
    };

    struct CallMethodCommand
    {
        int32_t id = 0;
        std::string method_name;
        std::vector<VariantValue> arguments;
    };

    struct LoadResourceCommand
    {
        int32_t id = 0;
        std::string path;
    };

    struct ReleaseResourceCommand
    {
        int32_t id = 0;
    };

    using FrameHandler = std::function<void(const FrameCommand& p_command)>;
    using Node2DHandler = std::function<void(const Node2DCommand& p_command)>;
    using CreateNodeHandler = std::function<void(const CreateNodeCommand& p_command)>;
    using DestroyObjectHandler = std::function<void(const DestroyObjectCommand& p_command)>;
    using SetParentHandler = std::function<void(const SetParentCommand& p_command)>;
    using SetTransform2DHandler = std::function<void(const SetTransform2DCommand& p_command)>;
    using SetVisibleHandler = std::function<void(const SetVisibleCommand& p_command)>;
    using SetPropertyHandler = std::function<void(const SetPropertyCommand& p_command)>;
    using CallMethodHandler = std::function<void(const CallMethodCommand& p_command)>;
    using LoadResourceHandler = std::function<void(const LoadResourceCommand& p_command)>;
    using ReleaseResourceHandler = std::function<void(const ReleaseResourceCommand& p_command)>;

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
        CallMethodHandler call_method;
        LoadResourceHandler load_resource;
        ReleaseResourceHandler release_resource;
    };

    bool read(const std::vector<uint8_t>& p_commands, const Handlers& p_handlers) const;
    bool read(const String& p_commands, const Handlers& p_handlers) const;
    bool read(const std::vector<uint8_t>& p_commands, const FrameHandler& p_frame_handler, const Node2DHandler& p_node_handler) const;
    bool read(const String& p_commands, const FrameHandler& p_frame_handler, const Node2DHandler& p_node_handler) const;
};

} // namespace godot
