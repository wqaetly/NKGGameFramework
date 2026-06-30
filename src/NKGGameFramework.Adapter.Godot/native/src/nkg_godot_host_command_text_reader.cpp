#include "nkg_godot_host_command_text_reader.h"

#include <sstream>

namespace
{
std::string to_std_string(const godot::String& value)
{
    const auto utf8 = value.utf8();
    return std::string(utf8.get_data(), utf8.length());
}
} // namespace

namespace godot
{

bool NkgGodotHostCommandTextReader::read(
    const String& p_commands,
    const FrameHandler& p_frame_handler,
    const Node2DHandler& p_node_handler) const
{
    std::istringstream stream(to_std_string(p_commands));
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
