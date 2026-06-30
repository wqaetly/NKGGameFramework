#pragma once

#include <cstdint>
#include <vector>

#include <godot_cpp/classes/ref_counted.hpp>
#include <godot_cpp/variant/packed_string_array.hpp>
#include <godot_cpp/variant/string.hpp>

#include "nkg_leanclr_managed_bridge.h"

namespace leanclr::metadata
{
class RtMethodInfo;
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
    std::vector<uint8_t> step_session_command_bytes();
    String handle_debug_request(const String& p_request);
    String get_status();
    String get_last_error() const;
    bool is_ready() const;

protected:
    static void _bind_methods();

private:
    bool ensure_ready();
    bool bind_managed_methods(NkgLeanClrRuntimeBridge& p_runtime);

    NkgLeanClrManagedBridge managed;
    const leanclr::metadata::RtMethodInfo* reset_method = nullptr;
    const leanclr::metadata::RtMethodInfo* clear_input_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_left_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_right_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_up_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_down_method = nullptr;
    const leanclr::metadata::RtMethodInfo* press_fire_method = nullptr;
    const leanclr::metadata::RtMethodInfo* step_method = nullptr;
    const leanclr::metadata::RtMethodInfo* step_bytes_method = nullptr;
    const leanclr::metadata::RtMethodInfo* debug_request_method = nullptr;
    const leanclr::metadata::RtMethodInfo* status_method = nullptr;
};

} // namespace godot
