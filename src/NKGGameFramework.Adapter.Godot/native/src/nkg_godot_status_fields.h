#pragma once

#include <cstdint>
#include <string>
#include <unordered_map>

#include <godot_cpp/variant/string.hpp>

namespace godot
{

class NkgGodotStatusFields
{
public:
    explicit NkgGodotStatusFields(const String& p_status);

    bool try_get_int(const std::string& p_key, int32_t& p_value) const;
    bool try_get_double(const std::string& p_key, double& p_value) const;

private:
    std::unordered_map<std::string, std::string> fields;
};

} // namespace godot
