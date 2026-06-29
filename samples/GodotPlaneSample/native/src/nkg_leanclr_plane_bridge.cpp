#include "nkg_leanclr_plane_bridge.h"

#include <cstdlib>
#include <fstream>

#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

#include "alloc/general_allocation.h"
#include "metadata/metadata_name.h"
#include "metadata/module_def.h"
#include "utils/string_builder.h"
#include "vm/assembly.h"
#include "vm/class.h"
#include "vm/method.h"
#include "vm/rt_exception.h"
#include "vm/rt_string.h"
#include "vm/runtime.h"
#include "vm/settings.h"

using namespace leanclr;

namespace
{
std::vector<std::string> g_library_dirs;
bool g_runtime_initialized = false;

std::string to_std_string(const godot::String& value)
{
    const auto utf8 = value.utf8();
    return std::string(utf8.get_data(), utf8.length());
}

bool assembly_file_loader(const char* assembly_name, const char* extension, vm::FileData& file_data)
{
    for (const auto& dir : g_library_dirs)
    {
        const std::string file_path = dir + "/" + assembly_name + "." + extension;
        std::ifstream dll_file(file_path, std::ios::binary | std::ios::ate);
        if (!dll_file.is_open())
        {
            continue;
        }

        const std::streamsize file_size = dll_file.tellg();
        dll_file.seekg(0, std::ios::beg);

        auto* dll_data = static_cast<uint8_t*>(alloc::GeneralAllocation::malloc(file_size));
        if (dll_data == nullptr)
        {
            return false;
        }

        if (!dll_file.read(reinterpret_cast<char*>(dll_data), file_size))
        {
            alloc::GeneralAllocation::free(dll_data);
            continue;
        }

        file_data.data = dll_data;
        file_data.length = static_cast<size_t>(file_size);
        file_data.shared = false;
        return true;
    }

    return false;
}

godot::String rt_string_to_godot(vm::RtString* value)
{
    if (value == nullptr)
    {
        return godot::String();
    }

    utils::Utf8StringBuilder builder;
    builder.append_utf16_str(&value->first_char, value->length);
    builder.sure_null_terminator_but_not_append();
    return godot::String::utf8(builder.get_const_chars());
}

const metadata::RtMethodInfo* find_static_method(metadata::RtModuleDef* module, const char* type_name, const char* method_name, size_t param_count)
{
    if (module == nullptr)
    {
        return nullptr;
    }

    auto class_result = module->get_class_by_nested_full_name(type_name, false, true);
    if (class_result.is_err() || class_result.unwrap() == nullptr)
    {
        return nullptr;
    }

    metadata::RtClass* klass = class_result.unwrap();
    if (vm::Class::initialize_all(klass).is_err())
    {
        return nullptr;
    }

    const metadata::RtMethodInfo* method = vm::Method::find_matched_method_in_class_by_name(klass, method_name);
    if (method == nullptr || !vm::Method::is_static(method) || vm::Method::get_param_count_include_this(method) != param_count)
    {
        return nullptr;
    }

    return method;
}
} // namespace

namespace godot
{

NkgLeanClrPlaneBridge::NkgLeanClrPlaneBridge()
{
    assembly_name = "NKGGameFramework.GodotPlaneSample";
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
    ClassDB::bind_method(D_METHOD("step_session"), &NkgLeanClrPlaneBridge::step_session);
    ClassDB::bind_method(D_METHOD("handle_debug_request", "request"), &NkgLeanClrPlaneBridge::handle_debug_request);
    ClassDB::bind_method(D_METHOD("get_status"), &NkgLeanClrPlaneBridge::get_status);
    ClassDB::bind_method(D_METHOD("get_last_error"), &NkgLeanClrPlaneBridge::get_last_error);
    ClassDB::bind_method(D_METHOD("is_ready"), &NkgLeanClrPlaneBridge::is_ready);
}

void NkgLeanClrPlaneBridge::configure(const PackedStringArray& p_library_dirs, const String& p_assembly_name)
{
    library_dirs = p_library_dirs;
    if (!p_assembly_name.is_empty())
    {
        assembly_name = p_assembly_name;
    }
}

bool NkgLeanClrPlaneBridge::initialize_runtime()
{
    last_error = String();
    if (library_dirs.is_empty())
    {
        set_error("LeanCLR library directories are empty.");
        return false;
    }

    g_library_dirs.clear();
    for (int64_t i = 0; i < library_dirs.size(); i++)
    {
        g_library_dirs.push_back(to_std_string(library_dirs[i]));
    }

    if (!g_runtime_initialized)
    {
        const std::string command_line_name = to_std_string(assembly_name);
        const char* args[] = {command_line_name.c_str()};
        vm::Settings::set_command_line_arguments(1, args);
        vm::Settings::set_file_loader(assembly_file_loader);

        auto init_result = vm::Runtime::initialize();
        if (init_result.is_err())
        {
            set_error("LeanCLR runtime initialization failed.");
            return false;
        }

        g_runtime_initialized = true;
    }

    auto assembly_result = vm::Assembly::load_by_name(to_std_string(assembly_name).c_str());
    if (assembly_result.is_err() || assembly_result.unwrap() == nullptr)
    {
        set_error("LeanCLR failed to load assembly: " + assembly_name);
        return false;
    }

    module = assembly_result.unwrap()->mod;
    if (!bind_managed_methods())
    {
        return false;
    }

    ready = invoke_void(reset_method);
    return ready;
}

void NkgLeanClrPlaneBridge::reset_session()
{
    if (!ready && !initialize_runtime())
    {
        return;
    }

    invoke_void(reset_method);
}

void NkgLeanClrPlaneBridge::clear_input()
{
    if (!ready && !initialize_runtime())
    {
        return;
    }

    invoke_void(clear_input_method);
}

void NkgLeanClrPlaneBridge::press_left()
{
    if (!ready && !initialize_runtime())
    {
        return;
    }

    invoke_void(press_left_method);
}

void NkgLeanClrPlaneBridge::press_right()
{
    if (!ready && !initialize_runtime())
    {
        return;
    }

    invoke_void(press_right_method);
}

void NkgLeanClrPlaneBridge::press_up()
{
    if (!ready && !initialize_runtime())
    {
        return;
    }

    invoke_void(press_up_method);
}

void NkgLeanClrPlaneBridge::press_down()
{
    if (!ready && !initialize_runtime())
    {
        return;
    }

    invoke_void(press_down_method);
}

void NkgLeanClrPlaneBridge::press_fire()
{
    if (!ready && !initialize_runtime())
    {
        return;
    }

    invoke_void(press_fire_method);
}

String NkgLeanClrPlaneBridge::step_session()
{
    if (!ready && !initialize_runtime())
    {
        return String();
    }

    return invoke_string(step_method);
}

String NkgLeanClrPlaneBridge::handle_debug_request(const String& p_request)
{
    if (!ready && !initialize_runtime())
    {
        return String("500\nInternal Server Error\napplication/json; charset=utf-8\n{\"message\":\"LeanCLR bridge is not ready.\"}");
    }

    return invoke_string_arg(debug_request_method, p_request);
}

String NkgLeanClrPlaneBridge::get_status()
{
    if (!ready && !initialize_runtime())
    {
        return last_error;
    }

    return invoke_string(status_method);
}

String NkgLeanClrPlaneBridge::get_last_error() const
{
    return last_error;
}

bool NkgLeanClrPlaneBridge::is_ready() const
{
    return ready;
}

bool NkgLeanClrPlaneBridge::bind_managed_methods()
{
    constexpr const char* type_name = "NKGGameFramework.GodotPlaneSample.PlaneGameBridge";
    reset_method = find_static_method(module, type_name, "ResetSession", 0);
    clear_input_method = find_static_method(module, type_name, "ClearInput", 0);
    press_left_method = find_static_method(module, type_name, "PressLeft", 0);
    press_right_method = find_static_method(module, type_name, "PressRight", 0);
    press_up_method = find_static_method(module, type_name, "PressUp", 0);
    press_down_method = find_static_method(module, type_name, "PressDown", 0);
    press_fire_method = find_static_method(module, type_name, "PressFire", 0);
    step_method = find_static_method(module, type_name, "StepSession", 0);
    debug_request_method = find_static_method(module, type_name, "HandleDebugRequest", 1);
    status_method = find_static_method(module, type_name, "GetSessionStatus", 0);

    if (reset_method == nullptr || clear_input_method == nullptr || press_left_method == nullptr ||
        press_right_method == nullptr || press_up_method == nullptr || press_down_method == nullptr ||
        press_fire_method == nullptr || step_method == nullptr || debug_request_method == nullptr || status_method == nullptr)
    {
        set_error("LeanCLR failed to bind PlaneGameBridge session/debug methods.");
        return false;
    }

    return true;
}

bool NkgLeanClrPlaneBridge::invoke_void(const metadata::RtMethodInfo* p_method)
{
    if (p_method == nullptr)
    {
        set_error("LeanCLR managed method is not bound.");
        return false;
    }

    auto invoke_result = vm::Runtime::invoke_array_arguments_with_run_cctor(p_method, nullptr, nullptr);
    if (invoke_result.is_err())
    {
        set_error("LeanCLR managed invocation failed.");
        return false;
    }

    return true;
}

String NkgLeanClrPlaneBridge::invoke_string(const metadata::RtMethodInfo* p_method)
{
    if (p_method == nullptr)
    {
        set_error("LeanCLR managed string method is not bound.");
        return String();
    }

    auto invoke_result = vm::Runtime::invoke_array_arguments_with_run_cctor(p_method, nullptr, nullptr);
    if (invoke_result.is_err())
    {
        set_error("LeanCLR managed string invocation failed.");
        return String();
    }

    return rt_string_to_godot(reinterpret_cast<vm::RtString*>(invoke_result.unwrap()));
}

String NkgLeanClrPlaneBridge::invoke_string_arg(const metadata::RtMethodInfo* p_method, const String& p_arg)
{
    if (p_method == nullptr)
    {
        set_error("LeanCLR managed string method is not bound.");
        return String();
    }

    const std::string arg = to_std_string(p_arg);
    vm::RtString* managed_arg = vm::String::create_string_from_utf8chars(arg.data(), static_cast<int32_t>(arg.size()));
    vm::RtObject* args[] = {reinterpret_cast<vm::RtObject*>(managed_arg)};
    auto invoke_result = vm::Runtime::invoke_object_arguments_with_run_cctor(p_method, nullptr, args, 1);
    if (invoke_result.is_err())
    {
        set_error("LeanCLR managed string invocation with argument failed.");
        return String("500\nInternal Server Error\napplication/json; charset=utf-8\n{\"message\":\"LeanCLR managed debug invocation failed.\"}");
    }

    return rt_string_to_godot(reinterpret_cast<vm::RtString*>(invoke_result.unwrap()));
}

void NkgLeanClrPlaneBridge::set_error(const String& p_message)
{
    last_error = p_message;
    UtilityFunctions::printerr("[NKG LeanCLR] ", p_message);
}

} // namespace godot
