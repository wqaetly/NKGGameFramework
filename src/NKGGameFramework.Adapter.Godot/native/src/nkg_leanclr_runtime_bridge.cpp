#include "nkg_leanclr_runtime_bridge.h"

#include <fstream>
#include <string>
#include <vector>

#include <godot_cpp/variant/utility_functions.hpp>

#include "alloc/general_allocation.h"
#include "metadata/module_def.h"
#include "utils/string_builder.h"
#include "vm/assembly.h"
#include "vm/rt_array.h"
#include "vm/class.h"
#include "vm/method.h"
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
} // namespace

namespace godot
{

void NkgLeanClrRuntimeBridge::configure(const PackedStringArray& p_library_dirs, const String& p_assembly_name)
{
    library_dirs = p_library_dirs;
    if (!p_assembly_name.is_empty())
    {
        assembly_name = p_assembly_name;
    }
}

bool NkgLeanClrRuntimeBridge::initialize_runtime()
{
    last_error = String();
    if (library_dirs.is_empty())
    {
        return fail("LeanCLR library directories are empty.");
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
            return fail("LeanCLR runtime initialization failed.");
        }

        g_runtime_initialized = true;
    }

    auto assembly_result = vm::Assembly::load_by_name(to_std_string(assembly_name).c_str());
    if (assembly_result.is_err() || assembly_result.unwrap() == nullptr)
    {
        return fail("LeanCLR failed to load assembly: " + assembly_name);
    }

    module = assembly_result.unwrap()->mod;
    return true;
}

bool NkgLeanClrRuntimeBridge::fail(const String& p_message)
{
    last_error = p_message;
    UtilityFunctions::printerr("[NKG LeanCLR] ", p_message);
    return false;
}

const metadata::RtMethodInfo* NkgLeanClrRuntimeBridge::find_static_method(
    const char* p_type_name,
    const char* p_method_name,
    size_t p_param_count) const
{
    if (module == nullptr)
    {
        return nullptr;
    }

    auto class_result = module->get_class_by_nested_full_name(p_type_name, false, true);
    if (class_result.is_err() || class_result.unwrap() == nullptr)
    {
        return nullptr;
    }

    metadata::RtClass* klass = class_result.unwrap();
    if (vm::Class::initialize_all(klass).is_err())
    {
        return nullptr;
    }

    const metadata::RtMethodInfo* method = vm::Method::find_matched_method_in_class_by_name(klass, p_method_name);
    if (method == nullptr || !vm::Method::is_static(method) ||
        vm::Method::get_param_count_include_this(method) != p_param_count)
    {
        return nullptr;
    }

    return method;
}

bool NkgLeanClrRuntimeBridge::invoke_void(const metadata::RtMethodInfo* p_method)
{
    if (p_method == nullptr)
    {
        return fail("LeanCLR managed method is not bound.");
    }

    auto invoke_result = vm::Runtime::invoke_array_arguments_with_run_cctor(p_method, nullptr, nullptr);
    if (invoke_result.is_err())
    {
        return fail("LeanCLR managed invocation failed.");
    }

    return true;
}

String NkgLeanClrRuntimeBridge::invoke_string(const metadata::RtMethodInfo* p_method)
{
    if (p_method == nullptr)
    {
        fail("LeanCLR managed string method is not bound.");
        return String();
    }

    auto invoke_result = vm::Runtime::invoke_array_arguments_with_run_cctor(p_method, nullptr, nullptr);
    if (invoke_result.is_err())
    {
        fail("LeanCLR managed string invocation failed.");
        return String();
    }

    return rt_string_to_godot(reinterpret_cast<vm::RtString*>(invoke_result.unwrap()));
}

bool NkgLeanClrRuntimeBridge::invoke_byte_array(const metadata::RtMethodInfo* p_method, std::vector<uint8_t>& p_output)
{
    p_output.clear();
    if (p_method == nullptr)
    {
        return fail("LeanCLR managed byte array method is not bound.");
    }

    auto invoke_result = vm::Runtime::invoke_array_arguments_with_run_cctor(p_method, nullptr, nullptr);
    if (invoke_result.is_err())
    {
        return fail("LeanCLR managed byte array invocation failed.");
    }

    auto* array = reinterpret_cast<vm::RtArray*>(invoke_result.unwrap());
    if (array == nullptr)
    {
        return fail("LeanCLR managed byte array invocation returned null.");
    }

    const int32_t length = vm::Array::get_array_length(array);
    if (length < 0)
    {
        return fail("LeanCLR managed byte array length was invalid.");
    }

    uint8_t* data = vm::Array::get_array_data_start_as<uint8_t>(array);
    p_output.assign(data, data + static_cast<size_t>(length));
    return true;
}

String NkgLeanClrRuntimeBridge::invoke_string_arg(
    const metadata::RtMethodInfo* p_method,
    const String& p_arg,
    const String& p_failure_response)
{
    if (p_method == nullptr)
    {
        fail("LeanCLR managed string method is not bound.");
        return p_failure_response;
    }

    const std::string arg = to_std_string(p_arg);
    vm::RtString* managed_arg = vm::String::create_string_from_utf8chars(arg.data(), static_cast<int32_t>(arg.size()));
    vm::RtObject* args[] = {reinterpret_cast<vm::RtObject*>(managed_arg)};
    auto invoke_result = vm::Runtime::invoke_object_arguments_with_run_cctor(p_method, nullptr, args, 1);
    if (invoke_result.is_err())
    {
        fail("LeanCLR managed string invocation with argument failed.");
        return p_failure_response;
    }

    return rt_string_to_godot(reinterpret_cast<vm::RtString*>(invoke_result.unwrap()));
}

String NkgLeanClrRuntimeBridge::get_last_error() const
{
    return last_error;
}

bool NkgLeanClrRuntimeBridge::is_runtime_ready() const
{
    return module != nullptr;
}

} // namespace godot
