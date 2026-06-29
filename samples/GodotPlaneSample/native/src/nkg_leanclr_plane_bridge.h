#pragma once

#include <string>
#include <vector>

#include <godot_cpp/classes/ref_counted.hpp>
#include <godot_cpp/templates/vector.hpp>
#include <godot_cpp/variant/packed_string_array.hpp>
#include <godot_cpp/variant/string.hpp>

namespace leanclr::metadata
{
class RtMethodInfo;
class RtModuleDef;
} // namespace leanclr::metadata

namespace godot
{

class NkgLeanClrPlaneBridge : public RefCounted
{
    GDCLASS(NkgLeanClrPlaneBridge, RefCounted)

public:
    NkgLeanClrPlaneBridge();
    ~NkgLeanClrPlaneBridge() override;

    void configure(const PackedStringArray& p_library_dirs, const String& p_assembly_name);
    bool initialize_runtime();
    void reset_session();
    void clear_input();
    void press_left();
    void press_right();
    void press_up();
    void press_down();
    void press_fire();
    String step_session();
    String handle_debug_request(const String& p_request);
    String get_status();
    String get_last_error() const;
    bool is_ready() const;

protected:
    static void _bind_methods();

private:
    bool bind_managed_methods();
    bool invoke_void(const leanclr::metadata::RtMethodInfo* p_method);
    String invoke_string(const leanclr::metadata::RtMethodInfo* p_method);
    String invoke_string_arg(const leanclr::metadata::RtMethodInfo* p_method, const String& p_arg);
    void set_error(const String& p_message);

    PackedStringArray library_dirs;
    String assembly_name;
    leanclr::metadata::RtModuleDef* module = nullptr;
    const leanclr::metadata::RtMethodInfo* reset_method = nullptr;
    const leanclr::metadata::RtMethodInfo* clear_input_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_left_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_right_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_up_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_down_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_fire_method = nullptr;
    const leanclr::metadata::RtMethodInfo* step_method = nullptr;
    const leanclr::metadata::RtMethodInfo* debug_request_method = nullptr;
    const leanclr::metadata::RtMethodInfo* status_method = nullptr;
    String last_error;
    bool ready = false;
};

} // namespace godot
