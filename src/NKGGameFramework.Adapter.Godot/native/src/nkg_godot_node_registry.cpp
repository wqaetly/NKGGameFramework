#include "nkg_godot_node_registry.h"

#include <vector>

namespace godot
{

void NkgGodotNodeRegistry::begin_frame()
{
    sync_frame++;
}

Node2D* NkgGodotNodeRegistry::sync_node(const std::string& p_key, const NodeFactory& p_factory)
{
    auto found = entries.find(p_key);
    if (found == entries.end())
    {
        Node2D* node = p_factory();
        if (node == nullptr)
        {
            return nullptr;
        }

        found = entries.emplace(p_key, Entry{node, sync_frame}).first;
    }

    found->second.last_seen = sync_frame;
    return found->second.node;
}

void NkgGodotNodeRegistry::remove_stale_nodes()
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

        if (found->second.node != nullptr)
        {
            found->second.node->queue_free();
        }
        entries.erase(found);
    }
}

size_t NkgGodotNodeRegistry::size() const
{
    return entries.size();
}

int32_t NkgGodotNodeRegistry::frame() const
{
    return sync_frame;
}

} // namespace godot
