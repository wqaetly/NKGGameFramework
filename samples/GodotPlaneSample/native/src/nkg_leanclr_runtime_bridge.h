#pragma once

#include <cstddef>

#include <godot_cpp/variant/packed_string_array.hpp>
#include <godot_cpp/variant/string.hpp>

namespace leanclr::metadata
{
class RtMethodInfo;
class RtModuleDef;
} // namespace leanclr::metadata

namespace godot
{

class NkgLeanClrRuntimeBridge
{
public:
    void configure(const PackedStringArray& p_library_dirs, const String& p_assembly_name);
    bool initialize_runtime();
    bool fail(const String& p_message);

    const leanclr::metadata::RtMethodInfo* find_static_method(
        const char* p_type_name,
        const char* p_method_name,
        size_t p_param_count) const;

    bool invoke_void(const leanclr::metadata::RtMethodInfo* p_method);
    String invoke_string(const leanclr::metadata::RtMethodInfo* p_method);
    String invoke_string_arg(
        const leanclr::metadata::RtMethodInfo* p_method,
        const String& p_arg,
        const String& p_failure_response = String());

    String get_last_error() const;
    bool is_runtime_ready() const;

private:
    PackedStringArray library_dirs;
    String assembly_name;
    leanclr::metadata::RtModuleDef* module = nullptr;
    String last_error;
};

} // namespace godot
