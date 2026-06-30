#include "nkg_godot_host_command_reader.h"

#include <cstring>
#include <sstream>
#include <vector>

namespace
{
constexpr const char* BINARY_ENVELOPE_PREFIX = "NKGCB1\nbase64\n";
constexpr uint8_t FRAME_COMMAND = 1;
constexpr uint8_t NODE2D_COMMAND = 2;
constexpr uint8_t CREATE_NODE_COMMAND = 3;
constexpr uint8_t DESTROY_OBJECT_COMMAND = 4;
constexpr uint8_t SET_PARENT_COMMAND = 5;
constexpr uint8_t SET_TRANSFORM2D_COMMAND = 6;
constexpr uint8_t SET_VISIBLE_COMMAND = 7;
constexpr uint8_t SET_PROPERTY_COMMAND = 8;
constexpr uint8_t END_COMMAND = 255;

std::string to_std_string(const godot::String& value)
{
    const auto utf8 = value.utf8();
    return std::string(utf8.get_data(), utf8.length());
}

int base64_value(char value)
{
    if (value >= 'A' && value <= 'Z')
    {
        return value - 'A';
    }
    if (value >= 'a' && value <= 'z')
    {
        return value - 'a' + 26;
    }
    if (value >= '0' && value <= '9')
    {
        return value - '0' + 52;
    }
    if (value == '+')
    {
        return 62;
    }
    if (value == '/')
    {
        return 63;
    }

    return -1;
}

bool base64_decode(const std::string& encoded, std::vector<uint8_t>& decoded)
{
    decoded.clear();
    int value = 0;
    int bits = -8;
    for (char ch : encoded)
    {
        if (ch == '=')
        {
            break;
        }

        const int digit = base64_value(ch);
        if (digit < 0)
        {
            continue;
        }

        value = (value << 6) + digit;
        bits += 6;
        if (bits >= 0)
        {
            decoded.push_back(static_cast<uint8_t>((value >> bits) & 0xff));
            bits -= 8;
        }
    }

    return true;
}

class BinaryCursor
{
public:
    explicit BinaryCursor(const std::vector<uint8_t>& p_data)
        : data(p_data)
    {
    }

    bool read_u8(uint8_t& value)
    {
        if (position >= data.size())
        {
            return false;
        }

        value = data[position++];
        return true;
    }

    bool read_i32(int32_t& value)
    {
        return read_pod(value);
    }

    bool read_i64(int64_t& value)
    {
        return read_pod(value);
    }

    bool read_f64(double& value)
    {
        return read_pod(value);
    }

    bool read_string(std::string& value)
    {
        int32_t length = 0;
        if (!read_i32(length) || length < 0 || position + static_cast<size_t>(length) > data.size())
        {
            return false;
        }

        value.assign(reinterpret_cast<const char*>(data.data() + position), static_cast<size_t>(length));
        position += static_cast<size_t>(length);
        return true;
    }

    bool read_variant(godot::NkgGodotHostCommandReader::VariantValue& value)
    {
        uint8_t kind = 0;
        if (!read_u8(kind))
        {
            return false;
        }

        if (kind == static_cast<uint8_t>(godot::NkgGodotHostCommandReader::VariantKind::Color))
        {
            value.kind = godot::NkgGodotHostCommandReader::VariantKind::Color;
            return read_f64(value.r) &&
                read_f64(value.g) &&
                read_f64(value.b) &&
                read_f64(value.a);
        }

        if (kind == static_cast<uint8_t>(godot::NkgGodotHostCommandReader::VariantKind::PackedVector2Array))
        {
            int32_t count = 0;
            if (!read_i32(count) || count < 0)
            {
                return false;
            }

            value.kind = godot::NkgGodotHostCommandReader::VariantKind::PackedVector2Array;
            value.vector2_array.clear();
            value.vector2_array.reserve(static_cast<size_t>(count));
            for (int32_t index = 0; index < count; index++)
            {
                godot::NkgGodotHostCommandReader::Vector2Value point;
                if (!read_f64(point.x) || !read_f64(point.y))
                {
                    return false;
                }
                value.vector2_array.push_back(point);
            }

            return true;
        }

        if (kind == static_cast<uint8_t>(godot::NkgGodotHostCommandReader::VariantKind::String))
        {
            value.kind = godot::NkgGodotHostCommandReader::VariantKind::String;
            return read_string(value.text);
        }

        if (kind == static_cast<uint8_t>(godot::NkgGodotHostCommandReader::VariantKind::Bool))
        {
            uint8_t boolean = 0;
            if (!read_u8(boolean))
            {
                return false;
            }

            value.kind = godot::NkgGodotHostCommandReader::VariantKind::Bool;
            value.boolean = boolean != 0;
            return true;
        }

        if (kind == static_cast<uint8_t>(godot::NkgGodotHostCommandReader::VariantKind::Integer))
        {
            value.kind = godot::NkgGodotHostCommandReader::VariantKind::Integer;
            return read_i64(value.integer);
        }

        if (kind == static_cast<uint8_t>(godot::NkgGodotHostCommandReader::VariantKind::Float))
        {
            value.kind = godot::NkgGodotHostCommandReader::VariantKind::Float;
            return read_f64(value.number);
        }

        if (kind == static_cast<uint8_t>(godot::NkgGodotHostCommandReader::VariantKind::Vector2))
        {
            value.kind = godot::NkgGodotHostCommandReader::VariantKind::Vector2;
            return read_f64(value.vector2.x) &&
                read_f64(value.vector2.y);
        }

        return false;
    }

private:
    template <typename TValue>
    bool read_pod(TValue& value)
    {
        if (position + sizeof(TValue) > data.size())
        {
            return false;
        }

        std::memcpy(&value, data.data() + position, sizeof(TValue));
        position += sizeof(TValue);
        return true;
    }

    const std::vector<uint8_t>& data;
    size_t position = 0;
};
} // namespace

namespace godot
{

bool NkgGodotHostCommandReader::read(
    const std::vector<uint8_t>& p_commands,
    const Handlers& p_handlers) const
{
    BinaryCursor cursor(p_commands);
    uint8_t opcode = 0;
    while (cursor.read_u8(opcode))
    {
        if (opcode == END_COMMAND)
        {
            return true;
        }

        if (opcode == FRAME_COMMAND)
        {
            FrameCommand command;
            uint8_t terminal = 0;
            if (!cursor.read_i32(command.frame) ||
                !cursor.read_i32(command.primary_value) ||
                !cursor.read_i32(command.secondary_value) ||
                !cursor.read_u8(terminal))
            {
                return false;
            }

            command.terminal = terminal != 0;
            if (p_handlers.frame)
            {
                p_handlers.frame(command);
            }
            continue;
        }

        if (opcode == NODE2D_COMMAND)
        {
            Node2DCommand command;
            if (!cursor.read_string(command.kind) ||
                !cursor.read_i32(command.id) ||
                !cursor.read_f64(command.x) ||
                !cursor.read_f64(command.y))
            {
                return false;
            }

            if (p_handlers.node2d)
            {
                p_handlers.node2d(command);
            }
            continue;
        }

        if (opcode == CREATE_NODE_COMMAND)
        {
            CreateNodeCommand command;
            if (!cursor.read_i32(command.id) ||
                !cursor.read_string(command.type_name) ||
                !cursor.read_string(command.name))
            {
                return false;
            }

            if (p_handlers.create_node)
            {
                p_handlers.create_node(command);
            }
            continue;
        }

        if (opcode == DESTROY_OBJECT_COMMAND)
        {
            DestroyObjectCommand command;
            if (!cursor.read_i32(command.id))
            {
                return false;
            }

            if (p_handlers.destroy_object)
            {
                p_handlers.destroy_object(command);
            }
            continue;
        }

        if (opcode == SET_PARENT_COMMAND)
        {
            SetParentCommand command;
            if (!cursor.read_i32(command.child_id) ||
                !cursor.read_i32(command.parent_id))
            {
                return false;
            }

            if (p_handlers.set_parent)
            {
                p_handlers.set_parent(command);
            }
            continue;
        }

        if (opcode == SET_TRANSFORM2D_COMMAND)
        {
            SetTransform2DCommand command;
            if (!cursor.read_i32(command.id) ||
                !cursor.read_f64(command.x) ||
                !cursor.read_f64(command.y) ||
                !cursor.read_f64(command.rotation) ||
                !cursor.read_f64(command.scale_x) ||
                !cursor.read_f64(command.scale_y))
            {
                return false;
            }

            if (p_handlers.set_transform2d)
            {
                p_handlers.set_transform2d(command);
            }
            continue;
        }

        if (opcode == SET_VISIBLE_COMMAND)
        {
            SetVisibleCommand command;
            uint8_t visible = 0;
            if (!cursor.read_i32(command.id) ||
                !cursor.read_u8(visible))
            {
                return false;
            }

            command.visible = visible != 0;
            if (p_handlers.set_visible)
            {
                p_handlers.set_visible(command);
            }
            continue;
        }

        if (opcode == SET_PROPERTY_COMMAND)
        {
            SetPropertyCommand command;
            if (!cursor.read_i32(command.id) ||
                !cursor.read_string(command.property_name) ||
                !cursor.read_variant(command.value))
            {
                return false;
            }

            if (p_handlers.set_property)
            {
                p_handlers.set_property(command);
            }
            continue;
        }

        return false;
    }

    return true;
}

bool NkgGodotHostCommandReader::read(
    const String& p_commands,
    const Handlers& p_handlers) const
{
    const std::string commands = to_std_string(p_commands);
    if (commands.rfind(BINARY_ENVELOPE_PREFIX, 0) == 0)
    {
        std::vector<uint8_t> payload;
        if (!base64_decode(commands.substr(std::strlen(BINARY_ENVELOPE_PREFIX)), payload))
        {
            return false;
        }

        return read(payload, p_handlers);
    }

    std::istringstream stream(commands);
    std::string tag;
    while (stream >> tag)
    {
        if (tag == "FRAME" || tag == "STATE")
        {
            FrameCommand command;
            int32_t terminal = 0;
            stream >> command.frame >> command.primary_value >> command.secondary_value >> terminal;
            command.terminal = terminal != 0;
            if (p_handlers.frame)
            {
                p_handlers.frame(command);
            }
            continue;
        }

        if (tag == "NODE2D")
        {
            Node2DCommand command;
            stream >> command.kind >> command.id >> command.x >> command.y;
            if (p_handlers.node2d)
            {
                p_handlers.node2d(command);
            }
            continue;
        }

        if (tag == "PLAYER" || tag == "ENEMY" || tag == "BULLET")
        {
            Node2DCommand command;
            command.kind = tag;
            stream >> command.id >> command.x >> command.y;
            if (p_handlers.node2d)
            {
                p_handlers.node2d(command);
            }
            continue;
        }

        if (tag == "CREATE_NODE")
        {
            CreateNodeCommand command;
            stream >> command.id >> command.type_name >> command.name;
            if (p_handlers.create_node)
            {
                p_handlers.create_node(command);
            }
            continue;
        }

        if (tag == "DESTROY_OBJECT")
        {
            DestroyObjectCommand command;
            stream >> command.id;
            if (p_handlers.destroy_object)
            {
                p_handlers.destroy_object(command);
            }
            continue;
        }

        if (tag == "SET_PARENT")
        {
            SetParentCommand command;
            stream >> command.child_id >> command.parent_id;
            if (p_handlers.set_parent)
            {
                p_handlers.set_parent(command);
            }
            continue;
        }

        if (tag == "SET_TRANSFORM2D")
        {
            SetTransform2DCommand command;
            stream >> command.id >> command.x >> command.y >> command.rotation >> command.scale_x >> command.scale_y;
            if (p_handlers.set_transform2d)
            {
                p_handlers.set_transform2d(command);
            }
            continue;
        }

        if (tag == "SET_VISIBLE")
        {
            SetVisibleCommand command;
            int32_t visible = 0;
            stream >> command.id >> visible;
            command.visible = visible != 0;
            if (p_handlers.set_visible)
            {
                p_handlers.set_visible(command);
            }
            continue;
        }

        if (tag == "SET_PROPERTY")
        {
            SetPropertyCommand command;
            std::string variant_kind;
            stream >> command.id >> command.property_name >> variant_kind;
            if (variant_kind == "COLOR")
            {
                command.value.kind = VariantKind::Color;
                stream >> command.value.r >> command.value.g >> command.value.b >> command.value.a;
            }
            else if (variant_kind == "PACKED_VECTOR2_ARRAY")
            {
                int32_t count = 0;
                command.value.kind = VariantKind::PackedVector2Array;
                stream >> count;
                if (count < 0)
                {
                    return false;
                }
                command.value.vector2_array.clear();
                command.value.vector2_array.reserve(static_cast<size_t>(count));
                for (int32_t index = 0; index < count; index++)
                {
                    Vector2Value point;
                    stream >> point.x >> point.y;
                    command.value.vector2_array.push_back(point);
                }
            }
            else if (variant_kind == "STRING")
            {
                std::string encoded;
                std::vector<uint8_t> decoded;
                command.value.kind = VariantKind::String;
                stream >> encoded;
                if (!base64_decode(encoded, decoded))
                {
                    return false;
                }
                command.value.text.assign(reinterpret_cast<const char*>(decoded.data()), decoded.size());
            }
            else if (variant_kind == "BOOL")
            {
                int32_t boolean = 0;
                command.value.kind = VariantKind::Bool;
                stream >> boolean;
                command.value.boolean = boolean != 0;
            }
            else if (variant_kind == "INTEGER")
            {
                command.value.kind = VariantKind::Integer;
                stream >> command.value.integer;
            }
            else if (variant_kind == "FLOAT")
            {
                command.value.kind = VariantKind::Float;
                stream >> command.value.number;
            }
            else if (variant_kind == "VECTOR2")
            {
                command.value.kind = VariantKind::Vector2;
                stream >> command.value.vector2.x >> command.value.vector2.y;
            }
            else
            {
                return false;
            }

            if (p_handlers.set_property)
            {
                p_handlers.set_property(command);
            }
            continue;
        }

        if (tag == "END")
        {
            return true;
        }
    }

    return true;
}

bool NkgGodotHostCommandReader::read(
    const std::vector<uint8_t>& p_commands,
    const FrameHandler& p_frame_handler,
    const Node2DHandler& p_node_handler) const
{
    Handlers handlers;
    handlers.frame = p_frame_handler;
    handlers.node2d = p_node_handler;
    return read(p_commands, handlers);
}

bool NkgGodotHostCommandReader::read(
    const String& p_commands,
    const FrameHandler& p_frame_handler,
    const Node2DHandler& p_node_handler) const
{
    Handlers handlers;
    handlers.frame = p_frame_handler;
    handlers.node2d = p_node_handler;
    return read(p_commands, handlers);
}

} // namespace godot
