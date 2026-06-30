#include "nkg_godot_input_pump.h"

#include <godot_cpp/classes/input.hpp>
#include <godot_cpp/variant/string_name.hpp>

namespace godot
{

void NkgGodotInputPump::pump_pressed_actions(const std::vector<ActionBinding>& p_bindings) const
{
    auto* input = Input::get_singleton();
    if (input == nullptr)
    {
        return;
    }

    for (const auto& binding : p_bindings)
    {
        if (binding.action_name == nullptr || !binding.handler)
        {
            continue;
        }

        if (input->is_action_pressed(StringName(binding.action_name)))
        {
            binding.handler();
        }
    }
}

} // namespace godot
