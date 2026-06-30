#include "nkg_godot_resource_registry.h"

namespace godot
{

void NkgGodotResourceRegistry::bind_resource(int32_t p_id, const Ref<Resource>& p_resource)
{
    resources[p_id] = p_resource;
}

Ref<Resource> NkgGodotResourceRegistry::get_resource(int32_t p_id) const
{
    auto found = resources.find(p_id);
    if (found == resources.end())
    {
        return Ref<Resource>();
    }

    return found->second;
}

bool NkgGodotResourceRegistry::release_resource(int32_t p_id)
{
    return resources.erase(p_id) > 0;
}

void NkgGodotResourceRegistry::clear()
{
    resources.clear();
}

size_t NkgGodotResourceRegistry::size() const
{
    return resources.size();
}

} // namespace godot
