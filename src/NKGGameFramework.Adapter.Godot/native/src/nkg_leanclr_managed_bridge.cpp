#include "nkg_leanclr_managed_bridge.h"

namespace godot
{

void NkgLeanClrManagedBridge::configure(
    const PackedStringArray& p_library_dirs,
    const String& p_assembly_name,
    const String& p_default_assembly_name)
{
    runtime.configure(p_library_dirs, p_assembly_name.is_empty() ? p_default_assembly_name : p_assembly_name);
    ready = false;
}

bool NkgLeanClrManagedBridge::initialize_runtime(const MethodBinder& p_bind_methods)
{
    ready = false;
    if (!runtime.initialize_runtime())
    {
        return false;
    }

    if (!p_bind_methods(runtime))
    {
        return false;
    }

    ready = true;
    return true;
}

bool NkgLeanClrManagedBridge::ensure_ready(const MethodBinder& p_bind_methods)
{
    return ready || initialize_runtime(p_bind_methods);
}

void NkgLeanClrManagedBridge::clear_ready()
{
    ready = false;
}

bool NkgLeanClrManagedBridge::invoke_void(const leanclr::metadata::RtMethodInfo* p_method)
{
    return runtime.invoke_void(p_method);
}

String NkgLeanClrManagedBridge::invoke_string(const leanclr::metadata::RtMethodInfo* p_method)
{
    return runtime.invoke_string(p_method);
}

bool NkgLeanClrManagedBridge::invoke_byte_array(const leanclr::metadata::RtMethodInfo* p_method, std::vector<uint8_t>& p_output)
{
    return runtime.invoke_byte_array(p_method, p_output);
}

String NkgLeanClrManagedBridge::invoke_string_arg(
    const leanclr::metadata::RtMethodInfo* p_method,
    const String& p_arg,
    const String& p_failure_response)
{
    return runtime.invoke_string_arg(p_method, p_arg, p_failure_response);
}

String NkgLeanClrManagedBridge::get_last_error() const
{
    return runtime.get_last_error();
}

bool NkgLeanClrManagedBridge::is_ready() const
{
    return ready;
}

} // namespace godot
