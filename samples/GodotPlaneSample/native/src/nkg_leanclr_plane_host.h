#pragma once

#include <string>
#include <vector>

#include <godot_cpp/classes/label.hpp>
#include <godot_cpp/classes/node2d.hpp>
#include <godot_cpp/classes/polygon2d.hpp>
#include <godot_cpp/variant/string.hpp>

#include "nkg_godot_host.h"
#include "nkg_leanclr_plane_bridge.h"

namespace godot
{

class NkgLeanClrPlaneHost : public Node2D
{
    GDCLASS(NkgLeanClrPlaneHost, Node2D)

public:
    NkgLeanClrPlaneHost();
    ~NkgLeanClrPlaneHost() override;

    void _ready() override;
    void _process(double p_delta) override;
    void _draw() override;

    String get_bridge_status() const;
    int32_t get_debug_port() const;
    int32_t get_object_count() const;
    int32_t get_bullet_count() const;
    int32_t get_max_bullet_count() const;
    double get_player_x() const;
    bool run_generic_property_smoke();

protected:
    static void _bind_methods();

private:
    void initialize_bridge();
    void initialize_debug_server();
    void process_debug_transport();
    void publish_debug_stream_snapshots();
    void pump_input();
    void apply_commands(const std::vector<uint8_t>& p_commands);
    void refresh_session_status();
    void update_hud();

    Ref<NkgLeanClrPlaneBridge> bridge;
    NkgGodotHost host;
    Label* hud_label = nullptr;
    String bridge_status = "boot";
    String managed_dir = "res://../NKGGameFramework.GodotPlaneSample/bin/Release/net10.0";
    String bcl_dir = "res://leanclr_bcl/net10.0";
    String assembly_name = "NKGGameFramework.GodotPlaneSample";
    int32_t debug_port = 5067;
    int32_t score = 0;
    int32_t lives = 0;
    int32_t bullet_count = 0;
    int32_t visual_object_count = 0;
    int32_t max_bullet_count = 0;
    double player_x = 160.0;
    double step_accumulator = 0.0;
};

} // namespace godot
