#include "nkg_godot_object_registry.h"

#include <vector>

namespace godot
{

void NkgGodotObjectRegistry::begin_frame()
{
    sync_frame++;
}

Object* NkgGodotObjectRegistry::sync_object(
    const std::string& p_key,
    const ObjectFactory& p_factory,
    const ObjectReleaser& p_releaser)
{
    auto found = entries.find(p_key);
    if (found == entries.end())
    {
        Object* object = p_factory();
        if (object == nullptr)
        {
            return nullptr;
        }

        found = entries.emplace(p_key, Entry{object, sync_frame, p_releaser}).first;
    }

    found->second.last_seen = sync_frame;
    return found->second.object;
}

Node2D* NkgGodotObjectRegistry::sync_node2d(const std::string& p_key, const Node2DFactory& p_factory)
{
    Object* object = sync_object(
        p_key,
        [&p_factory]() -> Object* {
            return p_factory();
        },
        [](Object* p_object) {
            static_cast<Node2D*>(p_object)->queue_free();
        });

    return static_cast<Node2D*>(object);
}

Object* NkgGodotObjectRegistry::get_object(const std::string& p_key) const
{
    auto found = entries.find(p_key);
    if (found == entries.end())
    {
        return nullptr;
    }

    return found->second.object;
}

void NkgGodotObjectRegistry::remove_stale_objects()
{
    std::vector<std::string> dead_keys;
    for (const auto& item : entries)
    {
        if (item.second.last_seen != sync_frame)
        {
            dead_keys.push_back(item.first);
        }
    }

    for (const auto& key : dead_keys)
    {
        auto found = entries.find(key);
        if (found == entries.end())
        {
            continue;
        }

        if (found->second.object != nullptr && found->second.releaser)
        {
            found->second.releaser(found->second.object);
        }
        entries.erase(found);
    }
}

size_t NkgGodotObjectRegistry::size() const
{
    return entries.size();
}

int32_t NkgGodotObjectRegistry::frame() const
{
    return sync_frame;
}

} // namespace godot
