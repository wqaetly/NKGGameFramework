extends SceneTree

var main_scene: Node
var elapsed := 0.0
var fired := false

func _init() -> void:
    var packed := load("res://scenes/main.tscn")
    if packed == null:
        printerr("NKG_MAIN_SCENE_SMOKE missing_scene")
        quit(2)
        return

    main_scene = packed.instantiate()
    root.add_child(main_scene)


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

    print("NKG_MAIN_SCENE_SMOKE ok " + status + " objects=" + str(objects) + " bullets=" + str(bullets) + " player_x=" + str(player_x))
    quit(0)
    return true
