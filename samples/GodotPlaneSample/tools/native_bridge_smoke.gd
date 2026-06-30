extends SceneTree

const MANAGED_DIR := "res://../NKGGameFramework.GodotPlaneSample/bin/Release/net10.0"
const BCL_DIR := "res://leanclr_bcl/net10.0"
const ASSEMBLY := "NKGGameFramework.GodotPlaneSample"

func _init() -> void:
    if !ClassDB.class_exists("NkgLeanClrPlaneBridge"):
        printerr("NKG_NATIVE_SMOKE missing_class")
        quit(2)
        return

    var bridge = ClassDB.instantiate("NkgLeanClrPlaneBridge")
    if bridge == null:
        printerr("NKG_NATIVE_SMOKE instantiate_failed")
        quit(3)
        return

    var managed_dir := ProjectSettings.globalize_path(MANAGED_DIR)
    var bcl_dir := ProjectSettings.globalize_path(BCL_DIR)
    bridge.configure(PackedStringArray([managed_dir, bcl_dir]), ASSEMBLY)

    if !bridge.initialize_runtime():
        printerr("NKG_NATIVE_SMOKE initialize_failed " + bridge.get_last_error())
        quit(4)
        return

    var line: String = bridge.step_session()
    if !line.begins_with("NKGCB1\nbase64\n"):
        printerr("NKG_NATIVE_SMOKE bad_state " + line)
        quit(5)
        return

    print("NKG_NATIVE_SMOKE ok " + line)
    quit(0)
