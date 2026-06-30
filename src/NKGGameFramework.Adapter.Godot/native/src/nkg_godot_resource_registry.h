#pragma once

#include <cstddef>
#include <cstdint>
#include <unordered_map>

#include <godot_cpp/classes/ref.hpp>
#include <godot_cpp/classes/resource.hpp>

namespace godot
{

class NkgGodotResourceRegistry
{
public:
    void bind_resource(int32_t p_id, const Ref<Resource>& p_resource);
    Ref<Resource> get_resource(int32_t p_id) const;
    bool release_resource(int32_t p_id);
    void clear();
    size_t size() const;

private:
    std::unordered_map<int32_t, Ref<Resource>> resources;
};

} // namespace godot
