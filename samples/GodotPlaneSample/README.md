# NKG LeanCLR Godot Plane Sample

This is a first desktop smoke path for connecting Godot to NKG through LeanCLR without using Godot's official C# extension.

Current shape:

- `NKGGameFramework.GodotPlaneSample` owns the plane-game logic in managed C# and uses NKG `RuntimeContext`, `World`, `Scene`, ECS systems and Godot adapter contracts.
- Godot 4.7 loads `NkgLeanClrPlaneHost` through GDExtension. The host embeds LeanCLR in-process, reads Godot input, calls managed `PlaneGameBridge.StepSession()` once per frame, and directly creates/updates Godot `Polygon2D` and `Label` objects.
- The sample uses a 1280x720 Godot viewport backed by a 640x360 gameplay arena, a 144 FPS / 144Hz fixed simulation target, deliberately slower player movement, readable enemy waves, and paced firing for a more presentable demo.
- `tools/stage-leanclr-bcl.ps1` copies the required `System*.dll` BCL assemblies into `leanclr_bcl/net10.0`, so Godot passes a project-local assembly path to LeanCLR instead of probing the machine-wide .NET shared framework at play time.
- The managed side does not reference `NKGGameFramework.Hosting`, so the debug HTTP/SSE transport and `System.Net` path stay out of this smoke.

Controls:

- Arrow keys: move the player plane.
- Space/Enter (`ui_accept`): fire.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\samples\GodotPlaneSample\tools\run-godot-plane.ps1
```

Headless verification:

```powershell
powershell -ExecutionPolicy Bypass -File .\samples\GodotPlaneSample\tools\run-godot-plane.ps1 -HeadlessCheck
```

Expected smoke output includes:

```text
NKG_MAIN_SCENE_SMOKE ok native object host ok objects=... bullets=... player_x=...
```

Native build inputs are kept outside this repository by default:

- `C:\study\wqaetly\new\leanclr`
- `C:\study\wqaetly\new\.cache\godot-cpp`
- `C:\study\wqaetly\new\.cache\godot-api-4.7\extension_api.json`

The next integration step is to replace the internal C# snapshot string with a typed ABI or generated host-service binding, then extend the same bridge to mobile/Web export templates.
