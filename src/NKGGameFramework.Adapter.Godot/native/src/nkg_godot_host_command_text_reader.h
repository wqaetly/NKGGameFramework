#pragma once

#include <cstdint>
#include <functional>
#include <string>

#include <godot_cpp/variant/string.hpp>

namespace godot
{

class NkgGodotHostCommandTextReader
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

    using FrameHandler = std::function<void(const FrameCommand& p_command)>;
    using Node2DHandler = std::function<void(const Node2DCommand& p_command)>;

    bool read(const String& p_commands, const FrameHandler& p_frame_handler, const Node2DHandler& p_node_handler) const;
};

} // namespace godot
