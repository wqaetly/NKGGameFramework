extends SceneTree

var main_scene: Node
var http: HTTPRequest
var elapsed := 0.0
var fired := false
var game_checked := false
var health_checked := false
var snapshot_checked := false
var structured_snapshot_checked := false
var control_pause_checked := false
var mutation_checked := false
var control_play_checked := false
var dump_recording_started := false
var dump_recording_stopped := false
var dump_analysis_checked := false
var dump_playback_checked := false
var dump_frame_checked := false
var dump_component_checked := false
var request_started := false
var debug_port := 0
var dump_stop_pending := false
var dump_stop_after := 0.0
var saved_dump_path := ""
var saved_dump_payload := PackedByteArray()
var playback_id := ""
var mutable_target := {}
var current_debug_path := ""

func _init() -> void:
    var packed := load("res://scenes/main.tscn")
    if packed == null:
        printerr("NKG_MAIN_SCENE_SMOKE missing_scene")
        quit(2)
        return

    main_scene = packed.instantiate()
    root.add_child(main_scene)
    http = HTTPRequest.new()
    http.request_completed.connect(_on_request_completed)
    root.add_child(http)


func _process(delta: float) -> bool:
    elapsed += delta

    if elapsed > 0.05 and !fired:
        Input.action_press("ui_right")
        Input.action_press("ui_accept")
        fired = true

    if elapsed > 1.05 and fired:
        Input.action_release("ui_right")
        Input.action_release("ui_accept")

    if elapsed < 1.2:
        return false

    if elapsed > 5.0:
        printerr("NKG_MAIN_SCENE_SMOKE debug_http_timeout")
        quit(7)
        return true

    if dump_stop_pending and elapsed >= dump_stop_after:
        dump_stop_pending = false
        _post_debug("/_nkg/debug/dump/recording", {"command": "stop"})
        return false

    if game_checked:
        return false

    var status: String = main_scene.get_bridge_status()
    var objects: int = main_scene.get_object_count()
    var bullets: int = main_scene.get_max_bullet_count()
    var player_x: float = main_scene.get_player_x()

    if !status.begins_with("native object host ok"):
        printerr("NKG_MAIN_SCENE_SMOKE bad_status " + status)
        quit(3)
        return true

    if objects < 7:
        printerr("NKG_MAIN_SCENE_SMOKE few_objects " + str(objects))
        quit(4)
        return true

    if bullets < 2:
        printerr("NKG_MAIN_SCENE_SMOKE no_bullet")
        quit(5)
        return true

    if player_x <= 390.0:
        printerr("NKG_MAIN_SCENE_SMOKE player_not_moved " + str(player_x))
        quit(6)
        return true

    debug_port = main_scene.get_debug_port()
    if debug_port <= 0:
        printerr("NKG_MAIN_SCENE_SMOKE debug_port_missing")
        quit(7)
        return true

    game_checked = true
    _request_debug("/_nkg/debug/health")
    return false


func _request_debug(path: String) -> void:
    request_started = true
    current_debug_path = path
    var err := http.request("http://127.0.0.1:" + str(debug_port) + path)
    if err != OK:
        printerr("NKG_MAIN_SCENE_SMOKE debug_http_request_failed " + str(err))
        quit(8)


func _post_debug(path: String, payload: Dictionary) -> void:
    request_started = true
    current_debug_path = path
    var body := JSON.stringify(payload)
    var headers := PackedStringArray(["Accept: application/json", "Content-Type: application/json"])
    var err := http.request("http://127.0.0.1:" + str(debug_port) + path, headers, HTTPClient.METHOD_POST, body)
    if err != OK:
        printerr("NKG_MAIN_SCENE_SMOKE debug_http_post_failed " + str(err))
        quit(8)


func _post_raw_debug(path: String, payload: PackedByteArray) -> void:
    request_started = true
    current_debug_path = path
    var headers := PackedStringArray(["Accept: application/json", "Content-Type: application/octet-stream"])
    var err := http.request_raw("http://127.0.0.1:" + str(debug_port) + path, headers, HTTPClient.METHOD_POST, payload)
    if err != OK:
        printerr("NKG_MAIN_SCENE_SMOKE debug_http_raw_post_failed " + str(err))
        quit(8)


func _parse_json_body(text: String) -> Dictionary:
    var parsed = JSON.parse_string(text)
    if typeof(parsed) != TYPE_DICTIONARY:
        printerr("NKG_MAIN_SCENE_SMOKE debug_json_bad_body " + text)
        quit(13)
        return {}
    return parsed


func _assert_success(text: String, label: String) -> Dictionary:
    var parsed := _parse_json_body(text)
    if !parsed.get("succeeded", false):
        printerr("NKG_MAIN_SCENE_SMOKE " + label + "_failed " + text)
        quit(14)
        return {}
    return parsed


func _find_first_component_target(snapshot: Dictionary) -> Dictionary:
    for world_value in snapshot.get("worlds", []):
        if typeof(world_value) != TYPE_DICTIONARY:
            continue

        var world: Dictionary = world_value
        for scene_value in world.get("scenes", []):
            if typeof(scene_value) != TYPE_DICTIONARY:
                continue

            var scene: Dictionary = scene_value
            for entity_value in scene.get("entities", []):
                if typeof(entity_value) != TYPE_DICTIONARY:
                    continue

                var entity: Dictionary = entity_value
                for component_value in entity.get("components", []):
                    if typeof(component_value) != TYPE_DICTIONARY:
                        continue

                    var component: Dictionary = component_value
                    var component_type: Dictionary = component.get("type", {})
                    return {
                        "worldName": world.get("name", ""),
                        "sceneName": scene.get("name", ""),
                        "entityId": entity.get("id", 0),
                        "componentTypeFullName": component_type.get("fullName", ""),
                        "componentAssemblyName": component_type.get("assemblyName", ""),
                    }

    return {}


func _on_request_completed(result: int, response_code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
    var text := body.get_string_from_utf8()
    if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
        printerr("NKG_MAIN_SCENE_SMOKE debug_http_bad_response path=" + current_debug_path + " result=" + str(result) + " code=" + str(response_code) + " body=" + text)
        quit(9)
        return

    if !health_checked:
        if !text.contains("\"status\":\"ok\""):
            printerr("NKG_MAIN_SCENE_SMOKE debug_health_bad_body " + text)
            quit(10)
            return

        health_checked = true
        _request_debug("/_nkg/debug/snapshot?includePayload=false&includeStructured=false&waitForFrame=false")
        return

    if !snapshot_checked:
        if !text.contains("\"worlds\"") or !text.contains("godot-plane-world"):
            printerr("NKG_MAIN_SCENE_SMOKE debug_snapshot_bad_body " + text)
            quit(11)
            return

        snapshot_checked = true
        _request_debug("/_nkg/debug/snapshot?componentTypeFullName=NKGGameFramework.GodotPlaneSample.Position&includePayload=true&includeStructured=true&entityLimit=1&waitForFrame=false")
        return

    if !structured_snapshot_checked:
        if !text.contains("\"format\":\"odin-json\"") or !text.contains("\"payload\"") or !text.contains("\"error\":null") or !text.contains("\"structured\"") or !text.contains("\"name\":\"X\""):
            printerr("NKG_MAIN_SCENE_SMOKE debug_structured_snapshot_bad_body " + text)
            quit(12)
            return

        structured_snapshot_checked = true
        var parsed := _parse_json_body(text)
        var world: Dictionary = parsed["snapshot"]["worlds"][0]
        var scene: Dictionary = world["scenes"][0]
        var entity: Dictionary = scene["entities"][0]
        var component: Dictionary = entity["components"][0]
        mutable_target = {
            "worldName": world["name"],
            "sceneName": scene["name"],
            "entityId": entity["id"],
            "entityVersion": entity["version"],
            "componentTypeFullName": component["type"]["fullName"],
            "componentAssemblyName": component["type"]["assemblyName"],
            "value": component["value"],
        }
        _post_debug("/_nkg/debug/control", {"command": "pause"})
        return

    if !control_pause_checked:
        _assert_success(text, "debug_control_pause")
        control_pause_checked = true
        _post_debug("/_nkg/debug/mutations", mutable_target)
        return

    if !mutation_checked:
        _assert_success(text, "debug_mutation")
        mutation_checked = true
        _post_debug("/_nkg/debug/control", {"command": "play"})
        return

    if !control_play_checked:
        _assert_success(text, "debug_control_play")
        control_play_checked = true
        _post_debug("/_nkg/debug/dump/recording", {"command": "start", "name": "godot-smoke-dump"})
        return

    if !dump_recording_started:
        _assert_success(text, "debug_dump_recording_start")
        dump_recording_started = true
        dump_stop_pending = true
        dump_stop_after = elapsed + 0.25
        return

    if !dump_recording_stopped:
        var stop := _assert_success(text, "debug_dump_recording_stop")
        dump_recording_stopped = true
        saved_dump_path = str(stop["state"]["lastDumpPath"])
        if saved_dump_path.is_empty():
            printerr("NKG_MAIN_SCENE_SMOKE debug_dump_missing_path " + text)
            quit(15)
            return

        saved_dump_payload = FileAccess.get_file_as_bytes(saved_dump_path)
        if saved_dump_payload.is_empty():
            printerr("NKG_MAIN_SCENE_SMOKE debug_dump_missing_payload " + saved_dump_path)
            quit(16)
            return

        _post_raw_debug("/_nkg/debug/dump/analysis/upload", saved_dump_payload)
        return

    if !dump_analysis_checked:
        var analysis := _parse_json_body(text)
        if int(analysis.get("frameCount", 0)) <= 0 or int(analysis.get("total", {}).get("totalBytes", 0)) <= 0:
            printerr("NKG_MAIN_SCENE_SMOKE debug_dump_analysis_bad_body " + text)
            quit(17)
            return

        dump_analysis_checked = true
        _post_raw_debug("/_nkg/debug/dump/playback/upload", saved_dump_payload)
        return

    if !dump_playback_checked:
        var playback := _parse_json_body(text)
        playback_id = str(playback.get("id", ""))
        if playback_id.is_empty() or int(playback.get("version", 0)) <= 0 or playback.get("frames", []).is_empty():
            printerr("NKG_MAIN_SCENE_SMOKE debug_dump_playback_bad_body " + text)
            quit(18)
            return

        dump_playback_checked = true
        _request_debug("/_nkg/debug/dump/playback/frame?playbackId=" + playback_id.uri_encode() + "&frameIndex=0")
        return

    if !dump_frame_checked:
        if !text.contains("\"snapshot\"") or !text.contains("\"worlds\""):
            printerr("NKG_MAIN_SCENE_SMOKE debug_dump_frame_bad_body " + text)
            quit(19)
            return

        dump_frame_checked = true
        var frame := _parse_json_body(text)
        var playback_component_target := _find_first_component_target(frame["snapshot"])
        if playback_component_target.is_empty():
            printerr("NKG_MAIN_SCENE_SMOKE debug_dump_frame_missing_component " + text)
            quit(20)
            return

        var component_path := "/_nkg/debug/dump/playback/component"
        component_path += "?playbackId=" + playback_id.uri_encode()
        component_path += "&frameIndex=0"
        component_path += "&worldName=" + str(playback_component_target["worldName"]).uri_encode()
        component_path += "&sceneName=" + str(playback_component_target["sceneName"]).uri_encode()
        component_path += "&entityId=" + str(int(playback_component_target["entityId"])).uri_encode()
        component_path += "&componentTypeFullName=" + str(playback_component_target["componentTypeFullName"]).uri_encode()
        component_path += "&componentAssemblyName=" + str(playback_component_target["componentAssemblyName"]).uri_encode()
        _request_debug(component_path)
        return

    if !dump_component_checked:
        if !text.contains("\"type\"") or !text.contains("\"value\"") or !text.contains("\"structured\""):
            printerr("NKG_MAIN_SCENE_SMOKE debug_dump_component_bad_body " + text)
            quit(21)
            return

        dump_component_checked = true
        print("NKG_MAIN_SCENE_SMOKE ok native object host ok debug http://127.0.0.1:" + str(debug_port))
        quit(0)
