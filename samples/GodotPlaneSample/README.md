# NKG LeanCLR Godot Plane Sample

This is a first desktop smoke path for connecting Godot to NKG through LeanCLR without using Godot's official C# extension.

Current shape:

- `NKGGameFramework.GodotPlaneSample` owns the plane-game logic in managed C# and uses NKG `RuntimeContext`, `World`, `Scene`, ECS systems and Godot adapter contracts.
- Godot 4.7 loads `NkgLeanClrPlaneHost` through GDExtension. The host embeds LeanCLR in-process, reads Godot input, calls managed `PlaneGameBridge.StepSessionCommandBytes()` on fixed simulation ticks, and applies the byte command buffer through the reusable native `NkgGodotHost`.
- The native GDExtension owns the desktop loopback WebDebug HTTP/SSE transport. Managed code only handles NKG debug commands and payloads; it does not open sockets.
- WebDebug uses `NKGGameFramework.Diagnostics` through the shared endpoint dispatcher. The native GDExtension only owns the desktop loopback HTTP/SSE transport; health, snapshot, stream, control, mutation, dump recording, dump analysis and dump playback all stay in managed Diagnostics code.
- The sample uses a 1280x720 Godot viewport backed by a 640x360 gameplay arena, a 144 FPS / 144Hz fixed simulation target, deliberately slower player movement, readable enemy waves, and paced firing for a more presentable demo.
- `tools/stage-leanclr-bcl.ps1` copies the runtime assembly closure required by the Godot managed output into `leanclr_bcl/net10.0`, so Godot passes a project-local assembly path to LeanCLR instead of probing the machine-wide .NET shared framework at play time.
- The managed side references `NKGGameFramework.Diagnostics` for the full debug domain, but it does not reference `NKGGameFramework.Hosting`; `System.Net` and managed HTTP/SSE stay out of this smoke.

Controls:

- Arrow keys: move the player plane.
- Space/Enter (`ui_accept`): fire.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\samples\GodotPlaneSample\run-godot-plane.ps1
```

The sample-local script performs a clean managed/native rebuild by default, stages the LeanCLR BCL closure, and then launches the Godot project. Use `-NoClean` for a faster incremental run or `-SkipNativeBuild` when only managed code changed.

Headless verification:

```powershell
powershell -ExecutionPolicy Bypass -File .\samples\GodotPlaneSample\run-godot-plane.ps1 -HeadlessCheck
```

Expected smoke output includes:

```text
NKG_MAIN_SCENE_SMOKE ok native object host ok debug http://127.0.0.1:5067
```

Native build inputs are kept outside this repository by default:

- `..\leanclr` relative to this repository when available
- `..\.cache\godot\4.7-stable\Godot_v4.7-stable_win64_console.exe`
- `..\.cache\godot-cpp`
- `..\.cache\godot-api-4.7\extension_api.json`

`tools/ensure-godot-4.7.ps1` downloads the official Godot 4.7 stable Windows build and clones/updates official `godot-cpp` when those local cache entries are missing.

The next integration step is to keep shrinking the remaining sample-specific host glue around HUD, input and status while expanding the generic property/Variant command coverage.
