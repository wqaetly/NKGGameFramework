#pragma once

#include <cstddef>
#include <cstdint>
#include <functional>
#include <string>
#include <unordered_map>

#include <godot_cpp/classes/node2d.hpp>
#include <godot_cpp/core/object.hpp>

namespace godot
{

class NkgGodotObjectRegistry
{
public:
    using ObjectFactory = std::function<Object*()>;
    using ObjectReleaser = std::function<void(Object* p_object)>;
    using Node2DFactory = std::function<Node2D*()>;

    void begin_frame();
    Object* sync_object(
        const std::string& p_key,
        const ObjectFactory& p_factory,
        const ObjectReleaser& p_releaser,
        bool p_remove_when_stale = false);
    Node2D* sync_node2d(const std::string& p_key, const Node2DFactory& p_factory);
    Object* get_object(const std::string& p_key) const;
    bool release_object(const std::string& p_key);
    void remove_stale_objects();
    size_t size() const;
    int32_t frame() const;

private:
    struct Entry
    {
        Object* object = nullptr;
        int32_t last_seen = 0;
        ObjectReleaser releaser;
        bool remove_when_stale = false;
    };

    std::unordered_map<std::string, Entry> entries;
    int32_t sync_frame = 0;
};

} // namespace godot
