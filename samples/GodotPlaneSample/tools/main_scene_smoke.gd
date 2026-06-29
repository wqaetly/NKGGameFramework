extends SceneTree

var main_scene: Node
var http: HTTPRequest
var elapsed := 0.0
var fired := false
var game_checked := false
var health_checked := false
var snapshot_checked := false
var request_started := false
var debug_port := 0

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
    var err := http.request("http://127.0.0.1:" + str(debug_port) + path)
    if err != OK:
        printerr("NKG_MAIN_SCENE_SMOKE debug_http_request_failed " + str(err))
        quit(8)


func _on_request_completed(result: int, response_code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
    var text := body.get_string_from_utf8()
    if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
        printerr("NKG_MAIN_SCENE_SMOKE debug_http_bad_response result=" + str(result) + " code=" + str(response_code) + " body=" + text)
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
        print("NKG_MAIN_SCENE_SMOKE ok native object host ok debug http://127.0.0.1:" + str(debug_port))
        quit(0)
