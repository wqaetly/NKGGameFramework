#include "nkg_leanclr_plane_host.h"

#include <sstream>
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

std::string to_std_string(const String& value)
{
    const auto utf8 = value.utf8();
    return std::string(utf8.get_data(), utf8.length());
}
} // namespace

NkgLeanClrPlaneHost::NkgLeanClrPlaneHost() = default;

NkgLeanClrPlaneHost::~NkgLeanClrPlaneHost()
{
    debug_server.stop();
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
    process_debug_requests();

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
        apply_snapshot(bridge->step_session());
        step_accumulator -= MANAGED_STEP_SECONDS;
        stepped = true;
        guard++;
    }

    if (stepped)
    {
        process_debug_requests();
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
        const double y = static_cast<double>((i * 83 + sync_tick * (1 + i % 3)) % static_cast<int32_t>(ARENA_HEIGHT * DISPLAY_SCALE));
        draw_circle(Vector2(x, y), 1.0 + static_cast<double>(i % 2), Color(0.7, 0.82, 0.92, 0.35));
    }
}

String NkgLeanClrPlaneHost::get_bridge_status() const
{
    return bridge_status;
}

int32_t NkgLeanClrPlaneHost::get_debug_port() const
{
    return static_cast<int32_t>(debug_server.get_port());
}

int32_t NkgLeanClrPlaneHost::get_object_count() const
{
    return static_cast<int32_t>(visuals.size());
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
    apply_snapshot(bridge->step_session());
    update_hud();
}

void NkgLeanClrPlaneHost::initialize_debug_server()
{
    if (!bridge.is_valid() || !bridge->is_ready())
    {
        return;
    }

    if (!debug_server.start(static_cast<uint16_t>(debug_port)))
    {
        bridge_status += " debug server failed: " + String(debug_server.get_last_error().c_str());
        return;
    }

    bridge_status += " debug http://127.0.0.1:" + String::num_int64(debug_server.get_port());
}

void NkgLeanClrPlaneHost::process_debug_requests()
{
    if (!debug_server.is_running())
    {
        return;
    }

    NkgDebugHttpServer::PendingRequest request;
    int32_t guard = 0;
    while (guard++ < 32 && debug_server.pop_pending_request(request))
    {
        if (!bridge.is_valid() || !bridge->is_ready())
        {
            debug_server.complete_request(
                request.id,
                NkgDebugHttpServer::Response{
                    503,
                    "Service Unavailable",
                    "application/json; charset=utf-8",
                    "{\"message\":\"LeanCLR bridge is not ready.\"}"});
            continue;
        }

        const std::string payload = request.method + "\n" + request.target + "\n" + request.body;
        const String managed_response = bridge->handle_debug_request(String::utf8(payload.c_str(), payload.size()));
        debug_server.complete_request(
            request.id,
            NkgDebugHttpServer::parse_managed_response(to_std_string(managed_response)));
    }
}

void NkgLeanClrPlaneHost::publish_debug_stream_snapshots()
{
    if (!debug_server.is_running() || !bridge.is_valid() || !bridge->is_ready())
    {
        return;
    }

    const auto targets = debug_server.get_stream_snapshot_targets();
    for (const auto& target : targets)
    {
        const std::string payload = "GET\n" + target + "\n";
        const String managed_response = bridge->handle_debug_request(String::utf8(payload.c_str(), payload.size()));
        const auto response = NkgDebugHttpServer::parse_managed_response(to_std_string(managed_response));
        if (response.status_code == 200)
        {
            debug_server.broadcast_snapshot(target, response.body);
        }
    }
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

void NkgLeanClrPlaneHost::apply_snapshot(const String& p_snapshot)
{
    if (p_snapshot.is_empty())
    {
        bridge_status = "empty snapshot";
        return;
    }

    sync_tick++;
    bullet_count = 0;

    std::istringstream stream(to_std_string(p_snapshot));
    std::string tag;
    while (stream >> tag)
    {
        if (tag == "STATE")
        {
            int32_t frame = 0;
            int32_t game_over = 0;
            stream >> frame >> score >> lives >> game_over;
            continue;
        }

        if (tag == "PLAYER" || tag == "ENEMY" || tag == "BULLET")
        {
            int32_t id = 0;
            double x = 0;
            double y = 0;
            stream >> id >> x >> y;
            sync_visual(String(tag.c_str()), id, x, y);
            if (tag == "PLAYER")
            {
                player_x = x;
            }
            else if (tag == "BULLET")
            {
                bullet_count++;
            }
            continue;
        }

        if (tag == "END")
        {
            break;
        }
    }

    remove_stale_visuals();
    if (bullet_count > max_bullet_count)
    {
        max_bullet_count = bullet_count;
    }
}

void NkgLeanClrPlaneHost::sync_visual(const String& p_kind, int32_t p_id, double p_x, double p_y)
{
    const std::string key = make_key(p_kind, p_id);
    auto found = visuals.find(key);
    if (found == visuals.end())
    {
        auto* visual = create_visual(p_kind, p_id);
        found = visuals.emplace(key, VisualObject{visual, p_kind, sync_tick}).first;
    }

    found->second.last_seen = sync_tick;
    found->second.node->set_position(Vector2(p_x * DISPLAY_SCALE, p_y * DISPLAY_SCALE));
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

void NkgLeanClrPlaneHost::remove_stale_visuals()
{
    std::vector<std::string> dead_keys;
    for (const auto& item : visuals)
    {
        if (item.second.last_seen != sync_tick)
        {
            dead_keys.push_back(item.first);
        }
    }

    for (const auto& key : dead_keys)
    {
        auto found = visuals.find(key);
        if (found == visuals.end())
        {
            continue;
        }

        if (found->second.node != nullptr)
        {
            found->second.node->queue_free();
        }
        visuals.erase(found);
    }
}

void NkgLeanClrPlaneHost::update_hud()
{
    if (hud_label == nullptr)
    {
        return;
    }

    hud_label->set_text(
        "Controls: arrows move  Space/Enter fire\nLeanCLR " + bridge_status +
        "\nWebDebug http://127.0.0.1:" + String::num_int64(debug_server.get_port()) +
        "\nscore " + String::num_int64(score) + "  lives " + String::num_int64(lives) +
        "  enemies/bullets " + String::num_int64(static_cast<int64_t>(visuals.size())));
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

std::string NkgLeanClrPlaneHost::make_key(const String& p_kind, int32_t p_id) const
{
    return to_std_string(p_kind) + ":" + std::to_string(p_id);
}

} // namespace godot
