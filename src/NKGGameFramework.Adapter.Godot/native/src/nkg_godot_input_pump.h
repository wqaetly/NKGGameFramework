#pragma once

#include <functional>
#include <vector>

namespace godot
{

class NkgGodotInputPump
{
public:
    using ActionHandler = std::function<void()>;

    struct ActionBinding
    {
        const char* action_name = nullptr;
        ActionHandler handler;
    };

    void pump_pressed_actions(const std::vector<ActionBinding>& p_bindings) const;
};

} // namespace godot
