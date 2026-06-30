#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <unordered_map>

#include <godot_cpp/classes/node2d.hpp>

namespace godot
{

class NkgGodotNodeRegistry
{
public:
    using NodeFactory = std::function<Node2D*()>;

    void begin_frame();
    Node2D* sync_node(const std::string& p_key, const NodeFactory& p_factory);
    void remove_stale_nodes();
    size_t size() const;
    int32_t frame() const;

private:
    struct Entry
    {
        Node2D* node = nullptr;
        int32_t last_seen = 0;
    };

    std::unordered_map<std::string, Entry> entries;
    int32_t sync_frame = 0;
};

} // namespace godot
