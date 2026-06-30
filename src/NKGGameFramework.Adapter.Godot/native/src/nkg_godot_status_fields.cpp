#include "nkg_godot_status_fields.h"

#include <sstream>

namespace godot
{

NkgGodotStatusFields::NkgGodotStatusFields(const String& p_status)
{
    const auto utf8 = p_status.utf8();
    std::istringstream stream(std::string(utf8.get_data(), utf8.length()));
    std::string token;
    while (stream >> token)
    {
        const size_t separator = token.find('=');
        if (separator == std::string::npos)
        {
            continue;
        }

        fields[token.substr(0, separator)] = token.substr(separator + 1);
    }
}

bool NkgGodotStatusFields::try_get_int(const std::string& p_key, int32_t& p_value) const
{
    auto found = fields.find(p_key);
    if (found == fields.end())
    {
        return false;
    }

    p_value = std::stoi(found->second);
    return true;
}

bool NkgGodotStatusFields::try_get_double(const std::string& p_key, double& p_value) const
{
    auto found = fields.find(p_key);
    if (found == fields.end())
    {
        return false;
    }

    p_value = std::stod(found->second);
    return true;
}

} // namespace godot
