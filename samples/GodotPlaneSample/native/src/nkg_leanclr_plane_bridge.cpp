#include "nkg_leanclr_plane_bridge.h"

#include <godot_cpp/core/class_db.hpp>

namespace godot
{

NkgLeanClrPlaneBridge::NkgLeanClrPlaneBridge()
{
    managed.configure(PackedStringArray(), "NKGGameFramework.GodotPlaneSample", "NKGGameFramework.GodotPlaneSample");
}

NkgLeanClrPlaneBridge::~NkgLeanClrPlaneBridge() = default;

void NkgLeanClrPlaneBridge::_bind_methods()
{
    ClassDB::bind_method(D_METHOD("configure", "library_dirs", "assembly_name"), &NkgLeanClrPlaneBridge::configure);
    ClassDB::bind_method(D_METHOD("initialize_runtime"), &NkgLeanClrPlaneBridge::initialize_runtime);
    ClassDB::bind_method(D_METHOD("reset_session"), &NkgLeanClrPlaneBridge::reset_session);
    ClassDB::bind_method(D_METHOD("clear_input"), &NkgLeanClrPlaneBridge::clear_input);
    ClassDB::bind_method(D_METHOD("press_left"), &NkgLeanClrPlaneBridge::press_left);
    ClassDB::bind_method(D_METHOD("press_right"), &NkgLeanClrPlaneBridge::press_right);
    ClassDB::bind_method(D_METHOD("press_up"), &NkgLeanClrPlaneBridge::press_up);
    ClassDB::bind_method(D_METHOD("press_down"), &NkgLeanClrPlaneBridge::press_down);
    ClassDB::bind_method(D_METHOD("press_fire"), &NkgLeanClrPlaneBridge::press_fire);
    ClassDB::bind_method(D_METHOD("set_host_context", "bridge_status", "debug_port"), &NkgLeanClrPlaneBridge::set_host_context);
    ClassDB::bind_method(D_METHOD("step_session"), &NkgLeanClrPlaneBridge::step_session);
    ClassDB::bind_method(D_METHOD("handle_debug_request", "request"), &NkgLeanClrPlaneBridge::handle_debug_request);
    ClassDB::bind_method(D_METHOD("get_status"), &NkgLeanClrPlaneBridge::get_status);
    ClassDB::bind_method(D_METHOD("get_last_error"), &NkgLeanClrPlaneBridge::get_last_error);
    ClassDB::bind_method(D_METHOD("is_ready"), &NkgLeanClrPlaneBridge::is_ready);
}

void NkgLeanClrPlaneBridge::configure(const PackedStringArray& p_library_dirs, const String& p_assembly_name)
{
    managed.configure(p_library_dirs, p_assembly_name, "NKGGameFramework.GodotPlaneSample");
}

bool NkgLeanClrPlaneBridge::initialize_runtime()
{
    if (!managed.initialize_runtime([this](NkgLeanClrRuntimeBridge& runtime) {
            return bind_managed_methods(runtime);
        }))
    {
        return false;
    }

    if (!managed.invoke_void(reset_method))
    {
        managed.clear_ready();
        return false;
    }

    return true;
}

void NkgLeanClrPlaneBridge::reset_session()
{
    if (!ensure_ready())
    {
        return;
    }

    managed.invoke_void(reset_method);
}

void NkgLeanClrPlaneBridge::clear_input()
{
    if (!ensure_ready())
    {
        return;
    }

    managed.invoke_void(clear_input_method);
}

void NkgLeanClrPlaneBridge::press_left()
{
    if (!ensure_ready())
    {
        return;
    }

    managed.invoke_void(press_left_method);
}

void NkgLeanClrPlaneBridge::press_right()
{
    if (!ensure_ready())
    {
        return;
    }

    managed.invoke_void(press_right_method);
}

void NkgLeanClrPlaneBridge::press_up()
{
    if (!ensure_ready())
    {
        return;
    }

    managed.invoke_void(press_up_method);
}

void NkgLeanClrPlaneBridge::press_down()
{
    if (!ensure_ready())
    {
        return;
    }

    managed.invoke_void(press_down_method);
}

void NkgLeanClrPlaneBridge::press_fire()
{
    if (!ensure_ready())
    {
        return;
    }

    managed.invoke_void(press_fire_method);
}

void NkgLeanClrPlaneBridge::set_host_context(const String& p_bridge_status, int32_t p_debug_port)
{
    if (!ensure_ready())
    {
        return;
    }

    managed.invoke_string_arg(host_context_method, p_bridge_status + String("\n") + String::num_int64(p_debug_port), "failed");
}

String NkgLeanClrPlaneBridge::step_session()
{
    if (!ensure_ready())
    {
        return String();
    }

    return managed.invoke_string(step_method);
}

std::vector<uint8_t> NkgLeanClrPlaneBridge::step_session_command_bytes()
{
    std::vector<uint8_t> buffer;
    if (!ensure_ready())
    {
        return buffer;
    }

    managed.invoke_byte_array(step_bytes_method, buffer);
    return buffer;
}

String NkgLeanClrPlaneBridge::handle_debug_request(const String& p_request)
{
    if (!ensure_ready())
    {
        return String("500\nInternal Server Error\napplication/json; charset=utf-8\n{\"message\":\"LeanCLR bridge is not ready.\"}");
    }

    return managed.invoke_string_arg(
        debug_request_method,
        p_request,
        "500\nInternal Server Error\napplication/json; charset=utf-8\n{\"message\":\"LeanCLR managed debug invocation failed.\"}");
}

String NkgLeanClrPlaneBridge::get_status()
{
    if (!ensure_ready())
    {
        return managed.get_last_error();
    }

    return managed.invoke_string(status_method);
}

String NkgLeanClrPlaneBridge::get_last_error() const
{
    return managed.get_last_error();
}

bool NkgLeanClrPlaneBridge::is_ready() const
{
    return managed.is_ready();
}

bool NkgLeanClrPlaneBridge::ensure_ready()
{
    return managed.ensure_ready([this](NkgLeanClrRuntimeBridge& runtime) {
        return bind_managed_methods(runtime);
    });
}

bool NkgLeanClrPlaneBridge::bind_managed_methods(NkgLeanClrRuntimeBridge& runtime)
{
    constexpr const char* type_name = "NKGGameFramework.GodotPlaneSample.PlaneGameBridge";
    reset_method = runtime.find_static_method(type_name, "ResetSession", 0);
    clear_input_method = runtime.find_static_method(type_name, "ClearInput", 0);
    press_left_method = runtime.find_static_method(type_name, "PressLeft", 0);
    press_right_method = runtime.find_static_method(type_name, "PressRight", 0);
    press_up_method = runtime.find_static_method(type_name, "PressUp", 0);
    press_down_method = runtime.find_static_method(type_name, "PressDown", 0);
    press_fire_method = runtime.find_static_method(type_name, "PressFire", 0);
    host_context_method = runtime.find_static_method(type_name, "UpdateHostContext", 1);
    step_method = runtime.find_static_method(type_name, "StepSession", 0);
    step_bytes_method = runtime.find_static_method(type_name, "StepSessionCommandBytes", 0);
    debug_request_method = runtime.find_static_method(type_name, "HandleDebugRequest", 1);
    status_method = runtime.find_static_method(type_name, "GetSessionStatus", 0);

    if (reset_method == nullptr || clear_input_method == nullptr || press_left_method == nullptr ||
        press_right_method == nullptr || press_up_method == nullptr || press_down_method == nullptr ||
        press_fire_method == nullptr || host_context_method == nullptr || step_method == nullptr || step_bytes_method == nullptr ||
        debug_request_method == nullptr || status_method == nullptr)
    {
        return runtime.fail("LeanCLR failed to bind PlaneGameBridge session/debug methods.");
    }

    return true;
}

} // namespace godot
