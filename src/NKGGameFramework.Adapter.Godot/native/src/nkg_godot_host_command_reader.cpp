#include "nkg_godot_host_command_reader.h"

#include <cstring>
#include <sstream>
#include <vector>

namespace
{
constexpr const char* BINARY_ENVELOPE_PREFIX = "NKGCB1\nbase64\n";
constexpr uint8_t FRAME_COMMAND = 1;
constexpr uint8_t NODE2D_COMMAND = 2;
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
    const String& p_commands,
    const FrameHandler& p_frame_handler,
    const Node2DHandler& p_node_handler) const
{
    const std::string commands = to_std_string(p_commands);
    if (commands.rfind(BINARY_ENVELOPE_PREFIX, 0) == 0)
    {
        std::vector<uint8_t> payload;
        if (!base64_decode(commands.substr(std::strlen(BINARY_ENVELOPE_PREFIX)), payload))
        {
            return false;
        }

        BinaryCursor cursor(payload);
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
                p_frame_handler(command);
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

                p_node_handler(command);
                continue;
            }

            return false;
        }

        return true;
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
            p_frame_handler(command);
            continue;
        }

        if (tag == "NODE2D")
        {
            Node2DCommand command;
            stream >> command.kind >> command.id >> command.x >> command.y;
            p_node_handler(command);
            continue;
        }

        if (tag == "PLAYER" || tag == "ENEMY" || tag == "BULLET")
        {
            Node2DCommand command;
            command.kind = tag;
            stream >> command.id >> command.x >> command.y;
            p_node_handler(command);
            continue;
        }

        if (tag == "END")
        {
            return true;
        }
    }

    return true;
}

} // namespace godot
