#include "nkg_leanclr_plane_host.h"

#include <cstdint>
#include <vector>

#include <godot_cpp/classes/input.hpp>
#include <godot_cpp/classes/project_settings.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/core/memory.hpp>
#include <godot_cpp/variant/rect2.hpp>
#include <godot_cpp/variant/string_name.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

namespace godot
{
namespace
{
constexpr double DISPLAY_SCALE = 2.0;
constexpr double ARENA_WIDTH = 640.0;
constexpr double ARENA_HEIGHT = 360.0;
constexpr double MANAGED_STEP_SECONDS = 1.0 / 144.0;
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
}

void NkgLeanClrPlaneHost::_ready()
{
    host.set_root(this);

    hud_label = memnew(Label);
    hud_label->set_name("Hud");
    hud_label->set_position(Vector2(14, 10));
    add_child(hud_label);

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
        apply_commands(bridge->step_session_command_bytes());
        step_accumulator -= MANAGED_STEP_SECONDS;
        stepped = true;
        guard++;
    }

    if (stepped)
    {
        process_debug_transport();
        publish_debug_stream_snapshots();
        update_hud();
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
    return static_cast<int32_t>(host.get_node_count());
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
    apply_commands(bridge->step_session_command_bytes());
    update_hud();
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

    auto* input = Input::get_singleton();
    if (input->is_action_pressed("ui_left"))
    {
        bridge->press_left();
    }
    if (input->is_action_pressed("ui_right"))
    {
        bridge->press_right();
    }
    if (input->is_action_pressed("ui_up"))
    {
        bridge->press_up();
    }
    if (input->is_action_pressed("ui_down"))
    {
        bridge->press_down();
    }
    if (input->is_action_pressed("ui_accept"))
    {
        bridge->press_fire();
    }
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
        [this](const NkgGodotHostCommandReader::Node2DCommand& command) {
            return create_visual(String(command.kind.c_str()), command.id);
        },
        [this](const NkgGodotHostCommandReader::Node2DCommand& command, Node2D* visual) {
            visual->set_position(Vector2(command.x * DISPLAY_SCALE, command.y * DISPLAY_SCALE));
            if (command.kind == "PLAYER")
            {
                player_x = command.x;
            }
            else if (command.kind == "BULLET")
            {
                bullet_count++;
            }
        });

    if (!applied)
    {
        bridge_status = "invalid command buffer";
        return;
    }

    if (bullet_count > max_bullet_count)
    {
        max_bullet_count = bullet_count;
    }
}

Polygon2D* NkgLeanClrPlaneHost::create_visual(const String& p_kind, int32_t p_id)
{
    auto* visual = memnew(Polygon2D);
    visual->set_name(p_kind + String("_") + String::num_int64(p_id));
    visual->set_polygon(make_polygon(p_kind));
    visual->set_color(color_for_kind(p_kind));
    add_child(visual);
    return visual;
}

void NkgLeanClrPlaneHost::update_hud()
{
    if (hud_label == nullptr)
    {
        return;
    }

    hud_label->set_text(
        "Controls: arrows move  Space/Enter fire\nLeanCLR " + bridge_status +
        "\nWebDebug http://127.0.0.1:" + String::num_int64(host.get_debug_port()) +
        "\nscore " + String::num_int64(score) + "  lives " + String::num_int64(lives) +
        "  enemies/bullets " + String::num_int64(static_cast<int64_t>(host.get_node_count())));
}

PackedVector2Array NkgLeanClrPlaneHost::make_polygon(const String& p_kind) const
{
    PackedVector2Array points;
    if (p_kind == "PLAYER")
    {
        points.push_back(Vector2(0, -36));
        points.push_back(Vector2(-10, 8));
        points.push_back(Vector2(-30, 28));
        points.push_back(Vector2(-6, 20));
        points.push_back(Vector2(0, 12));
        points.push_back(Vector2(6, 20));
        points.push_back(Vector2(30, 28));
        points.push_back(Vector2(10, 8));
        return points;
    }

    if (p_kind == "ENEMY")
    {
        points.push_back(Vector2(-28, -16));
        points.push_back(Vector2(28, -16));
        points.push_back(Vector2(34, 8));
        points.push_back(Vector2(12, 24));
        points.push_back(Vector2(0, 16));
        points.push_back(Vector2(-12, 24));
        points.push_back(Vector2(-34, 8));
        return points;
    }

    points.push_back(Vector2(0, -14));
    points.push_back(Vector2(6, 0));
    points.push_back(Vector2(0, 14));
    points.push_back(Vector2(-6, 0));
    return points;
}

Color NkgLeanClrPlaneHost::color_for_kind(const String& p_kind) const
{
    if (p_kind == "PLAYER")
    {
        return Color(0.2, 0.78, 0.95);
    }

    if (p_kind == "ENEMY")
    {
        return Color(0.96, 0.25, 0.24);
    }

    return Color(1.0, 0.93, 0.36);
}

} // namespace godot
