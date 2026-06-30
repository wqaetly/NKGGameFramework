#pragma once

#include <functional>
#include <vector>

#include <godot_cpp/variant/packed_string_array.hpp>
#include <godot_cpp/variant/string.hpp>

#include "nkg_leanclr_runtime_bridge.h"

namespace leanclr::metadata
{
class RtMethodInfo;
} // namespace leanclr::metadata

namespace godot
{

class NkgLeanClrManagedBridge
{
public:
    using MethodBinder = std::function<bool(NkgLeanClrRuntimeBridge& p_runtime)>;

    void configure(const PackedStringArray& p_library_dirs, const String& p_assembly_name, const String& p_default_assembly_name);
    bool initialize_runtime(const MethodBinder& p_bind_methods);
    bool ensure_ready(const MethodBinder& p_bind_methods);
    void clear_ready();

    bool invoke_void(const leanclr::metadata::RtMethodInfo* p_method);
    String invoke_string(const leanclr::metadata::RtMethodInfo* p_method);
    bool invoke_byte_array(const leanclr::metadata::RtMethodInfo* p_method, std::vector<uint8_t>& p_output);
    String invoke_string_arg(
        const leanclr::metadata::RtMethodInfo* p_method,
        const String& p_arg,
        const String& p_failure_response = String());

    String get_last_error() const;
    bool is_ready() const;

private:
    NkgLeanClrRuntimeBridge runtime;
    bool ready = false;
};

} // namespace godot
