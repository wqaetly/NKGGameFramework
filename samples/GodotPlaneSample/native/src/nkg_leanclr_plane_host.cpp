#include "nkg_leanclr_plane_host.h"

#include <cstdint>
#include <cstring>
#include <vector>

#include <godot_cpp/classes/label.hpp>
#include <godot_cpp/classes/project_settings.hpp>
#include <godot_cpp/classes/polygon2d.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/core/math.hpp>
#include <godot_cpp/core/memory.hpp>
#include <godot_cpp/variant/color.hpp>
#include <godot_cpp/variant/packed_vector2_array.hpp>
#include <godot_cpp/variant/rect2.hpp>
#include <godot_cpp/variant/string_name.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

#include "nkg_godot_status_fields.h"

namespace godot
{
namespace
{
constexpr double DISPLAY_SCALE = 2.0;
constexpr double ARENA_WIDTH = 640.0;
constexpr double ARENA_HEIGHT = 360.0;
constexpr double MANAGED_STEP_SECONDS = 1.0 / 144.0;

void write_u8(std::vector<uint8_t>& buffer, uint8_t value)
{
    buffer.push_back(value);
}

void write_i32(std::vector<uint8_t>& buffer, int32_t value)
{
    const auto* bytes = reinterpret_cast<const uint8_t*>(&value);
    buffer.insert(buffer.end(), bytes, bytes + sizeof(value));
}

void write_f64(std::vector<uint8_t>& buffer, double value)
{
    const auto* bytes = reinterpret_cast<const uint8_t*>(&value);
    buffer.insert(buffer.end(), bytes, bytes + sizeof(value));
}

void write_string(std::vector<uint8_t>& buffer, const char* value)
{
    const int32_t length = static_cast<int32_t>(std::strlen(value));
    write_i32(buffer, length);
    buffer.insert(buffer.end(), value, value + length);
}
} // namespace

NkgLeanClrPlaneHost::NkgLeanClrPlaneHost() = default;

NkgLeanClrPlaneHost::~NkgLeanClrPlaneHost()
{
    host.stop_debug_transport();
}

void NkgLeanClrPlaneHost::_bind_methods()
{
    ClassDB::bind_method(D_METHOD("get_bridge_status"), &NkgLeanClrPlaneHost::get_bridge_status);
    ClassDB::bind_method(D_METHOD("get_debug_port"), &NkgLeanClrPlaneHost::get_debug_port);
    ClassDB::bind_method(D_METHOD("get_object_count"), &NkgLeanClrPlaneHost::get_object_count);
    ClassDB::bind_method(D_METHOD("get_bullet_count"), &NkgLeanClrPlaneHost::get_bullet_count);
    ClassDB::bind_method(D_METHOD("get_max_bullet_count"), &NkgLeanClrPlaneHost::get_max_bullet_count);
    ClassDB::bind_method(D_METHOD("get_player_x"), &NkgLeanClrPlaneHost::get_player_x);
    ClassDB::bind_method(D_METHOD("run_generic_property_smoke"), &NkgLeanClrPlaneHost::run_generic_property_smoke);
}

void NkgLeanClrPlaneHost::_ready()
{
    host.set_root(this);

    initialize_bridge();
    initialize_debug_server();
    set_process(true);
}

void NkgLeanClrPlaneHost::_process(double p_delta)
{
    process_debug_transport();

    if (!bridge.is_valid() || !bridge->is_ready())
    {
        queue_redraw();
        return;
    }

    step_accumulator += p_delta;
    bool stepped = false;
    int32_t guard = 0;
    while (step_accumulator >= MANAGED_STEP_SECONDS && guard < 4)
    {
        pump_input();
        publish_host_context();
        apply_commands(bridge->step_session_command_bytes());
        step_accumulator -= MANAGED_STEP_SECONDS;
        stepped = true;
        guard++;
    }

    if (stepped)
    {
        process_debug_transport();
        publish_debug_stream_snapshots();
    }
    queue_redraw();
}

void NkgLeanClrPlaneHost::_draw()
{
    draw_rect(Rect2(Vector2(0, 0), Vector2(ARENA_WIDTH * DISPLAY_SCALE, ARENA_HEIGHT * DISPLAY_SCALE)), Color(0.035, 0.05, 0.07), true);
    for (int32_t i = 0; i < 90; i++)
    {
        const double x = static_cast<double>((i * 47) % static_cast<int32_t>(ARENA_WIDTH * DISPLAY_SCALE));
        const double y = static_cast<double>((i * 83 + host.get_frame() * (1 + i % 3)) % static_cast<int32_t>(ARENA_HEIGHT * DISPLAY_SCALE));
        draw_circle(Vector2(x, y), 1.0 + static_cast<double>(i % 2), Color(0.7, 0.82, 0.92, 0.35));
    }
}

String NkgLeanClrPlaneHost::get_bridge_status() const
{
    return bridge_status;
}

int32_t NkgLeanClrPlaneHost::get_debug_port() const
{
    return static_cast<int32_t>(host.get_debug_port());
}

int32_t NkgLeanClrPlaneHost::get_object_count() const
{
    return visual_object_count;
}

int32_t NkgLeanClrPlaneHost::get_bullet_count() const
{
    return bullet_count;
}

int32_t NkgLeanClrPlaneHost::get_max_bullet_count() const
{
    return max_bullet_count;
}

double NkgLeanClrPlaneHost::get_player_x() const
{
    return player_x;
}

bool NkgLeanClrPlaneHost::run_generic_property_smoke()
{
    constexpr int32_t object_id = 990001;
    constexpr int32_t label_id = 990002;
    std::vector<uint8_t> commands;

    write_u8(commands, 3);
    write_i32(commands, object_id);
    write_string(commands, "Polygon2D");
    write_string(commands, "GenericPropertySmoke");

    write_u8(commands, 8);
    write_i32(commands, object_id);
    write_string(commands, "polygon");
    write_u8(commands, 2);
    write_i32(commands, 3);
    write_f64(commands, 0.0);
    write_f64(commands, -8.0);
    write_f64(commands, 8.0);
    write_f64(commands, 8.0);
    write_f64(commands, -8.0);
    write_f64(commands, 8.0);

    write_u8(commands, 8);
    write_i32(commands, object_id);
    write_string(commands, "color");
    write_u8(commands, 1);
    write_f64(commands, 0.25);
    write_f64(commands, 0.5);
    write_f64(commands, 0.75);
    write_f64(commands, 1.0);

    write_u8(commands, 6);
    write_i32(commands, object_id);
    write_f64(commands, 32.0);
    write_f64(commands, 48.0);
    write_f64(commands, 0.0);
    write_f64(commands, 1.0);
    write_f64(commands, 1.0);

    write_u8(commands, 7);
    write_i32(commands, object_id);
    write_u8(commands, 1);

    write_u8(commands, 3);
    write_i32(commands, label_id);
    write_string(commands, "Label");
    write_string(commands, "GenericLabelSmoke");

    write_u8(commands, 8);
    write_i32(commands, label_id);
    write_string(commands, "text");
    write_u8(commands, 3);
    write_string(commands, "generic label ok");

    write_u8(commands, 255);

    const bool applied = host.apply_commands(
        commands,
        [](const NkgGodotHostCommandReader::FrameCommand&) {},
        [](const NkgGodotHostCommandReader::Node2DCommand&) -> Node2D* {
            return nullptr;
        },
        [](const NkgGodotHostCommandReader::Node2DCommand&, Node2D*) {});
    if (!applied)
    {
        return false;
    }

    auto* node = Object::cast_to<Polygon2D>(find_child("GenericPropertySmoke", false, false));
    if (node == nullptr)
    {
        return false;
    }

    auto* label = Object::cast_to<Label>(find_child("GenericLabelSmoke", false, false));
    if (label == nullptr || label->get_text() != "generic label ok")
    {
        return false;
    }

    const PackedVector2Array polygon = node->get_polygon();
    const Color color = node->get_color();
    return polygon.size() == 3 &&
        node->get_position().is_equal_approx(Vector2(32.0, 48.0)) &&
        Math::is_equal_approx(color.r, 0.25f) &&
        Math::is_equal_approx(color.g, 0.5f) &&
        Math::is_equal_approx(color.b, 0.75f) &&
        Math::is_equal_approx(color.a, 1.0f);
}

void NkgLeanClrPlaneHost::initialize_bridge()
{
    bridge.instantiate();
    if (!bridge.is_valid())
    {
        bridge_status = "bridge instantiate failed";
        return;
    }

    auto* settings = ProjectSettings::get_singleton();
    PackedStringArray library_dirs;
    library_dirs.push_back(settings->globalize_path(managed_dir));
    library_dirs.push_back(settings->globalize_path(bcl_dir));
    bridge->configure(library_dirs, assembly_name);

    if (!bridge->initialize_runtime())
    {
        bridge_status = "native " + bridge->get_last_error();
        bridge.unref();
        return;
    }

    bridge_status = "native object host ok";
    publish_host_context();
    apply_commands(bridge->step_session_command_bytes());
}

void NkgLeanClrPlaneHost::initialize_debug_server()
{
    if (!bridge.is_valid() || !bridge->is_ready())
    {
        return;
    }

    if (!host.start_debug_transport(static_cast<uint16_t>(debug_port)))
    {
        bridge_status += " debug server failed: " + String(host.get_debug_last_error().c_str());
        return;
    }

    bridge_status += " debug http://127.0.0.1:" + String::num_int64(host.get_debug_port());
    publish_host_context();
}

void NkgLeanClrPlaneHost::process_debug_transport()
{
    const bool bridge_ready = bridge.is_valid() && bridge->is_ready();
    host.process_debug_transport(bridge_ready, [this](const String& request) {
        return bridge->handle_debug_request(request);
    });
}

void NkgLeanClrPlaneHost::publish_debug_stream_snapshots()
{
    const bool bridge_ready = bridge.is_valid() && bridge->is_ready();
    host.publish_debug_stream_snapshots(bridge_ready, [this](const String& request) {
        return bridge->handle_debug_request(request);
    });
}

void NkgLeanClrPlaneHost::pump_input()
{
    bridge->clear_input();

    input_pump.pump_pressed_actions({
        {"ui_left", [this]() { bridge->press_left(); }},
        {"ui_right", [this]() { bridge->press_right(); }},
        {"ui_up", [this]() { bridge->press_up(); }},
        {"ui_down", [this]() { bridge->press_down(); }},
        {"ui_accept", [this]() { bridge->press_fire(); }},
    });
}

void NkgLeanClrPlaneHost::publish_host_context()
{
    if (!bridge.is_valid() || !bridge->is_ready())
    {
        return;
    }

    bridge->set_host_context(bridge_status, static_cast<int32_t>(host.get_debug_port()));
}

void NkgLeanClrPlaneHost::apply_commands(const std::vector<uint8_t>& p_commands)
{
    if (p_commands.empty())
    {
        bridge_status = "empty command buffer";
        return;
    }

    bullet_count = 0;

    const bool applied = host.apply_commands(
        p_commands,
        [this](const NkgGodotHostCommandReader::FrameCommand& command) {
            score = command.primary_value;
            lives = command.secondary_value;
        },
        [](const NkgGodotHostCommandReader::Node2DCommand&) -> Node2D* {
            return nullptr;
        },
        [](const NkgGodotHostCommandReader::Node2DCommand&, Node2D*) {});

    if (!applied)
    {
        bridge_status = "invalid command buffer";
        return;
    }

    refresh_session_status();
    if (bullet_count > max_bullet_count)
    {
        max_bullet_count = bullet_count;
    }
}

void NkgLeanClrPlaneHost::refresh_session_status()
{
    if (!bridge.is_valid() || !bridge->is_ready())
    {
        return;
    }

    const NkgGodotStatusFields status(bridge->get_status());
    status.try_get_double("player_x", player_x);
    status.try_get_int("bullets", bullet_count);
    status.try_get_int("objects", visual_object_count);
}

} // namespace godot
