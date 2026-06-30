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

            PlaneGameBridge.StepSession();
            PlaneGameBridge.StepSession();

            var stop = SendJson<GameDebugDumpRecordingResult>(
                "POST",
                "/_nkg/debug/dump/recording",
                new GameDebugDumpRecordingRequest("stop"));
            Assert.True(stop.Succeeded, stop.Message);
            savedPath = stop.State.LastDumpPath;
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
