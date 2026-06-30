using System.Text;
using System.Text.Json;
using NKGGameFramework.Adapter.Godot;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.GodotPlaneSample;

namespace NKGGameFramework.Tests.Hosting;

[Collection(GameDebugRegistryCollection.Name)]
public sealed class GodotPlaneDebugBridgeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Godot_debug_endpoint_bridge_uses_godot_runtime_defaults()
    {
        var options = GodotDebugEndpointBridge.CreateDispatcherOptions();

        Assert.Equal("/_nkg/debug", options.EndpointPrefix);
        Assert.False(options.DefaultWaitForSnapshotFrame);
        Assert.True(options.EnableMutations);
        Assert.Same(GameDebugController.Shared, options.Control);
        Assert.Same(GameDebugFramePublisher.Shared, options.Frames);
    }

    [Fact]
    public void Godot_plane_bridge_exposes_direct_command_bytes()
    {
        PlaneGameBridge.ResetSession();

        var bytes = PlaneGameBridge.StepSessionCommandBytes();

        Assert.NotEmpty(bytes);
        Assert.Equal(1, bytes[0]);
        Assert.Equal(255, bytes[^1]);
    }

    [Fact]
    public void Godot_plane_bridge_outputs_hud_label_through_generic_commands()
    {
        PlaneGameBridge.ResetSession();
        PlaneGameBridge.UpdateHostContext("native object host ok debug http://127.0.0.1:5067\n5067");

        var bytes = PlaneGameBridge.StepSessionCommandBytes();
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Label", text, StringComparison.Ordinal);
        Assert.Contains("Hud", text, StringComparison.Ordinal);
        Assert.Contains("Controls: arrows move", text, StringComparison.Ordinal);
        Assert.Contains("WebDebug http://127.0.0.1:5067", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Godot_plane_bridge_reports_world_update_inside_runtime_frame_metrics()
    {
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            PlaneGameBridge.ResetSession();
            PlaneGameBridge.StepSession();

            var snapshot = SendJson<GameDebugSnapshotMessage>(
                "GET",
                "/_nkg/debug/snapshot?includePayload=false&includeStructured=false&waitForFrame=false");
            var runtime = Assert.Single(snapshot.Snapshot.Runtimes);

            Assert.Contains(
                runtime.Modules,
                module => module.IsUpdateModule &&
                    module.Type.Name.Contains("WorldUpdateModule", StringComparison.Ordinal));
            Assert.NotNull(snapshot.Frame.Metrics);
            Assert.True(snapshot.Frame.Metrics.LogicMilliseconds > 0d);
        }
        finally
        {
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public void Godot_bridge_routes_full_webdebug_endpoints_through_diagnostics_dispatcher()
    {
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();
        string? savedPath = null;

        try
        {
            PlaneGameBridge.ResetSession();
            PlaneGameBridge.StepSession();

            var health = Send("GET", "/_nkg/debug/health");
            Assert.Equal(200, health.StatusCode);
            Assert.Contains("\"status\":\"ok\"", health.Body, StringComparison.Ordinal);

            var snapshot = SendJson<GameDebugSnapshotMessage>(
                "GET",
                "/_nkg/debug/snapshot?includePayload=true&includeStructured=true&waitForFrame=false");
            var target = FindMutableComponent(snapshot);

            var pause = SendJson<GameDebugControlResult>(
                "POST",
                "/_nkg/debug/control",
                new GameDebugControlRequest("pause"));
            Assert.True(pause.Succeeded);
            Assert.True(pause.State.IsPaused);

            var mutation = SendJson<GameDebugMutationResult>(
                "POST",
                "/_nkg/debug/mutations",
                new GameDebugMutationRequest(
                    target.World.Name,
                    target.Scene.Name,
                    target.Entity.Id,
                    target.Entity.Version,
                    target.Component.Type.FullName,
                    target.Component.Type.AssemblyName,
                    target.Component.Value));
            Assert.True(mutation.Succeeded, mutation.Message);

            var play = SendJson<GameDebugControlResult>(
                "POST",
                "/_nkg/debug/control",
                new GameDebugControlRequest("play"));
            Assert.True(play.Succeeded);

            var start = SendJson<GameDebugDumpRecordingResult>(
                "POST",
                "/_nkg/debug/dump/recording",
                new GameDebugDumpRecordingRequest("start", "godot-bridge-dump-test"));
            Assert.True(start.Succeeded);
            Assert.True(start.State.IsRecording);

            for (var index = 0; index < 5; index++)
            {
                PlaneGameBridge.StepSession();
            }

            var stop = SendJson<GameDebugDumpRecordingResult>(
                "POST",
                "/_nkg/debug/dump/recording",
                new GameDebugDumpRecordingRequest("stop"));
            Assert.True(stop.Succeeded, stop.Message);
            savedPath = WaitForDumpPath();
            Assert.False(string.IsNullOrWhiteSpace(savedPath));
            Assert.True(File.Exists(savedPath));

            var payload = File.ReadAllBytes(savedPath);
            var analysis = SendJson<GameDebugDumpAnalysisReport>(
                "POST",
                "/_nkg/debug/dump/analysis/upload",
                payload);
            Assert.True(analysis.FrameCount >= 2);
            Assert.True(analysis.Total.TotalBytes > 0);

            var playback = SendJson<GameDebugDumpPlaybackManifest>(
                "POST",
                "/_nkg/debug/dump/playback/upload",
                payload);
            Assert.True(playback.Frames.Count >= 2);

            var frame = SendJson<GameDebugSnapshotMessage>(
                "GET",
                $"/_nkg/debug/dump/playback/frame?playbackId={playback.Id}&frameIndex=0");
            Assert.NotEmpty(frame.Snapshot.Worlds);

            var component = SendJson<ComponentDebugSnapshot>(
                "GET",
                "/_nkg/debug/dump/playback/component" +
                $"?playbackId={playback.Id}" +
                "&frameIndex=0" +
                $"&worldName={Uri.EscapeDataString(target.World.Name)}" +
                $"&sceneName={Uri.EscapeDataString(target.Scene.Name)}" +
                $"&entityId={target.Entity.Id}" +
                $"&componentTypeFullName={Uri.EscapeDataString(target.Component.Type.FullName)}" +
                $"&componentAssemblyName={Uri.EscapeDataString(target.Component.Type.AssemblyName)}");
            Assert.Equal(target.Component.Type.FullName, component.Type.FullName);
            Assert.NotNull(component.Value.Structured);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
            {
                File.Delete(savedPath);
            }

            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public void Godot_dump_playback_component_detail_uses_the_requested_frame_block()
    {
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();
        string? savedPath = null;

        try
        {
            PlaneGameBridge.ResetSession();
            PlaneGameBridge.StepSession();

            var snapshot = SendJson<GameDebugSnapshotMessage>(
                "GET",
                "/_nkg/debug/snapshot?includePayload=false&includeStructured=false&waitForFrame=false");
            var target = FindEnemyPositionComponent(snapshot);

            var start = SendJson<GameDebugDumpRecordingResult>(
                "POST",
                "/_nkg/debug/dump/recording",
                new GameDebugDumpRecordingRequest("start", "godot-position-frame-test"));
            Assert.True(start.Succeeded);

            for (var index = 0; index < 9; index++)
            {
                PlaneGameBridge.StepSession();
            }

            var stop = SendJson<GameDebugDumpRecordingResult>(
                "POST",
                "/_nkg/debug/dump/recording",
                new GameDebugDumpRecordingRequest("stop"));
            Assert.True(stop.Succeeded, stop.Message);
            savedPath = WaitForDumpPath();
            Assert.False(string.IsNullOrWhiteSpace(savedPath));

            var playback = SendJson<GameDebugDumpPlaybackManifest>(
                "POST",
                "/_nkg/debug/dump/playback/upload",
                File.ReadAllBytes(savedPath));
            Assert.True(playback.Frames.Count >= 3);

            var first = GetPlaybackComponent(playback.Id, frameIndex: 0, target);
            var last = GetPlaybackComponent(playback.Id, playback.Frames.Count - 1, target);
            var firstX = ReadStructuredNumber(first, "X");
            var firstY = ReadStructuredNumber(first, "Y");
            var lastX = ReadStructuredNumber(last, "X");
            var lastY = ReadStructuredNumber(last, "Y");

            Assert.True(
                Math.Abs(lastX - firstX) > 0.000001d || Math.Abs(lastY - firstY) > 0.000001d,
                $"Expected enemy Position to change across dump frames, but first=({firstX},{firstY}) last=({lastX},{lastY}).");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
            {
                File.Delete(savedPath);
            }

            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    private static MutableComponentTarget FindMutableComponent(GameDebugSnapshotMessage snapshot)
    {
        foreach (var world in snapshot.Snapshot.Worlds)
        {
            foreach (var scene in world.Scenes)
            {
                foreach (var entity in scene.Entities)
                {
                    foreach (var component in entity.Components)
                    {
                        if (!string.IsNullOrWhiteSpace(component.Value.Payload))
                        {
                            return new MutableComponentTarget(world, scene, entity, component);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("The Godot plane snapshot did not expose a component payload to mutate.");
    }

    private static MutableComponentTarget FindEnemyPositionComponent(GameDebugSnapshotMessage snapshot)
    {
        foreach (var world in snapshot.Snapshot.Worlds)
        {
            foreach (var scene in world.Scenes)
            {
                foreach (var entity in scene.Entities)
                {
                    var hasEnemyTag = entity.Components.Any(static component =>
                        StringComparer.Ordinal.Equals(component.Type.Name, "EnemyTag"));
                    if (!hasEnemyTag)
                    {
                        continue;
                    }

                    var position = entity.Components.SingleOrDefault(static component =>
                        StringComparer.Ordinal.Equals(component.Type.Name, "Position"));
                    if (position is not null)
                    {
                        return new MutableComponentTarget(world, scene, entity, position);
                    }
                }
            }
        }

        throw new InvalidOperationException("The Godot plane snapshot did not expose an enemy Position component.");
    }

    private static ComponentDebugSnapshot GetPlaybackComponent(
        string playbackId,
        int frameIndex,
        MutableComponentTarget target)
    {
        return SendJson<ComponentDebugSnapshot>(
            "GET",
            "/_nkg/debug/dump/playback/component" +
            $"?playbackId={playbackId}" +
            $"&frameIndex={frameIndex}" +
            $"&worldName={Uri.EscapeDataString(target.World.Name)}" +
            $"&sceneName={Uri.EscapeDataString(target.Scene.Name)}" +
            $"&entityId={target.Entity.Id}" +
            $"&componentTypeFullName={Uri.EscapeDataString(target.Component.Type.FullName)}" +
            $"&componentAssemblyName={Uri.EscapeDataString(target.Component.Type.AssemblyName)}");
    }

    private static double ReadStructuredNumber(ComponentDebugSnapshot component, string name)
    {
        var child = FindChild(component.Value.Structured!, name);
        return double.Parse(child.Value!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ComponentValueDebugNode FindChild(ComponentValueDebugNode node, string name)
    {
        return node.Children.Single(child => StringComparer.Ordinal.Equals(child.Name, name));
    }

    private static string WaitForDumpPath()
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            var state = SendJson<GameDebugDumpRecordingState>(
                "GET",
                "/_nkg/debug/dump/recording");
            if (!string.IsNullOrWhiteSpace(state.LastDumpError))
            {
                throw new InvalidOperationException(state.LastDumpError);
            }

            if (!state.IsFinalizing && !string.IsNullOrWhiteSpace(state.LastDumpPath))
            {
                return state.LastDumpPath;
            }

            Thread.Sleep(25);
        }

        throw new TimeoutException("Timed out waiting for the debug dump recording to finish saving.");
    }

    private static T SendJson<T>(string method, string target, object? body = null)
    {
        var payload = body is null
            ? []
            : JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        return SendJson<T>(method, target, payload);
    }

    private static T SendJson<T>(string method, string target, byte[] body)
    {
        var response = Send(method, target, body);
        Assert.Equal(200, response.StatusCode);
        return JsonSerializer.Deserialize<T>(response.Body, JsonOptions)
            ?? throw new JsonException($"Bridge response could not be deserialized as {typeof(T).Name}.");
    }

    private static BridgeResponse Send(string method, string target)
    {
        return Send(method, target, []);
    }

    private static BridgeResponse Send(string method, string target, byte[] body)
    {
        var request = method + "\n" + target + "\nbase64\n" + Convert.ToBase64String(body);
        return ParseResponse(PlaneGameBridge.HandleDebugRequest(request));
    }

    private static BridgeResponse ParseResponse(string value)
    {
        var first = value.IndexOf('\n', StringComparison.Ordinal);
        var second = first < 0 ? -1 : value.IndexOf('\n', first + 1);
        var third = second < 0 ? -1 : value.IndexOf('\n', second + 1);
        if (first < 0 || second < 0 || third < 0)
        {
            throw new InvalidDataException("Managed debug bridge response was malformed.");
        }

        return new BridgeResponse(
            int.Parse(value[..first], System.Globalization.CultureInfo.InvariantCulture),
            value[(first + 1)..second],
            value[(second + 1)..third],
            value[(third + 1)..]);
    }

    private sealed record BridgeResponse(
        int StatusCode,
        string ReasonPhrase,
        string ContentType,
        string Body);

    private sealed record MutableComponentTarget(
        WorldDebugSnapshot World,
        SceneDebugSnapshot Scene,
        EntityDebugSnapshot Entity,
        ComponentDebugSnapshot Component);
}
