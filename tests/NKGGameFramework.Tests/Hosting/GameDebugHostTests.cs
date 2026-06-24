using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using NKGGameFramework.Core;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.Ecs;
using NKGGameFramework.Hosting.Diagnostics;

namespace NKGGameFramework.Tests.Hosting;

[Collection(GameDebugRegistryCollection.Name)]
public sealed class GameDebugHostTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task Host_serves_snapshot_from_framework_registry()
    {
        GameDebugRuntimeRegistry.Clear();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("host-debug-world");
            var scene = world.CreateScene("battle");
            var entity = scene.CreateEntity()
                .Add(new PositionComponent(12, 34));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };

            var message = await client.GetFromJsonAsync<GameDebugSnapshotMessage>(
                "/_nkg/debug/snapshot",
                JsonOptions);

            Assert.NotNull(message);
            Assert.Equal("snapshot", message.Frame.Source);
            Assert.Contains(message.Snapshot.Runtimes, runtimeSnapshot => runtimeSnapshot.IsDisposed is false);
            var worldSnapshot = Assert.Single(
                message.Snapshot.Worlds,
                worldSnapshot => worldSnapshot.Name == world.Name);
            var sceneSnapshot = Assert.Single(worldSnapshot.Scenes);
            Assert.Equal(scene.Name, sceneSnapshot.Name);
            Assert.Contains(sceneSnapshot.Entities, entitySnapshot => entitySnapshot.Id == entity.Id.Value);
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
        }
    }

    [Fact]
    public async Task Host_accepts_debug_control_commands()
    {
        GameDebugController.Shared.Reset();

        try
        {
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };

            var pauseResponse = await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("pause"),
                JsonOptions);
            var pause = await pauseResponse.Content.ReadFromJsonAsync<GameDebugControlResult>(JsonOptions);

            Assert.True(pauseResponse.IsSuccessStatusCode);
            Assert.NotNull(pause);
            Assert.True(pause.Succeeded);
            Assert.True(pause.State.IsPaused);

            var stepResponse = await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("step", StepCount: 1),
                JsonOptions);
            var step = await stepResponse.Content.ReadFromJsonAsync<GameDebugControlResult>(JsonOptions);

            Assert.True(stepResponse.IsSuccessStatusCode);
            Assert.NotNull(step);
            Assert.True(step.Succeeded);
            Assert.Equal(1, step.State.PendingStepCount);

            var state = await client.GetFromJsonAsync<GameDebugControlState>(
                "/_nkg/debug/control",
                JsonOptions);

            Assert.NotNull(state);
            Assert.True(state.IsPaused);
            Assert.Equal(1, state.PendingStepCount);
        }
        finally
        {
            GameDebugController.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_debug_control_commands_gate_framework_updates()
    {
        GameDebugController.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            var module = runtime.RegisterModule(new CountingUpdateModule());
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };

            await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("pause"),
                JsonOptions);
            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            Assert.Equal(0, module.UpdateCount);
            Assert.Equal(0, runtime.Time.Frame);

            await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("step"),
                JsonOptions);
            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, runtime.Time.Frame);

            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, runtime.Time.Frame);

            await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("play"),
                JsonOptions);
            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            Assert.Equal(2, module.UpdateCount);
            Assert.Equal(2, runtime.Time.Frame);
        }
        finally
        {
            GameDebugController.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_snapshot_requests_observe_framework_changes_after_each_runtime_frame()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("auto-debug-world");
            var scene = world.CreateScene("battle");
            var tracked = scene.CreateEntity()
                .Add(new PositionComponent(0, 0));
            runtime.RegisterModule(new FrameMutationModule(scene, tracked));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };

            for (var frame = 1; frame <= 3; frame++)
            {
                runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame));

                var summary = await client.GetFromJsonAsync<GameDebugSnapshotMessage>(
                    CreateSnapshotPath(world, scene, includePayload: false, includeStructured: false),
                    JsonOptions);
                var summaryScene = FindSceneSnapshot(summary?.Snapshot, world, scene);
                var positionStore = Assert.Single(
                    summaryScene.ComponentStores,
                    store => store.Type.Name == nameof(PositionComponent));
                var trackedSummary = Assert.Single(
                    summaryScene.Entities,
                    entity => entity.Id == tracked.Id.Value);
                var trackedSummaryComponent = Assert.Single(
                    trackedSummary.Components,
                    component => component.Type.Name == nameof(PositionComponent));

                Assert.Equal(frame + 1, summaryScene.EntityCount);
                Assert.Equal(frame + 1, positionStore.Count);
                Assert.Null(trackedSummaryComponent.Value.Payload);
                Assert.Null(trackedSummaryComponent.Value.Structured);

                var detail = await client.GetFromJsonAsync<GameDebugSnapshotMessage>(
                    CreateSnapshotPath(
                        world,
                        scene,
                        includePayload: true,
                        includeStructured: true,
                        entityId: tracked.Id.Value),
                    JsonOptions);
                var detailScene = FindSceneSnapshot(detail?.Snapshot, world, scene);
                var trackedDetail = Assert.Single(detailScene.Entities);
                var trackedDetailComponent = Assert.Single(
                    trackedDetail.Components,
                    component => component.Type.Name == nameof(PositionComponent));
                Assert.NotNull(trackedDetailComponent.Value.Structured);
                var structured = trackedDetailComponent.Value.Structured!;
                var x = FindChild(structured, nameof(PositionComponent.X));
                var y = FindChild(structured, nameof(PositionComponent.Y));

                Assert.NotNull(x.Value);
                Assert.NotNull(y.Value);
                Assert.Equal(frame, double.Parse(x.Value!, CultureInfo.InvariantCulture));
                Assert.Equal(frame * 10, double.Parse(y.Value!, CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_stream_pushes_summary_snapshot_after_framework_frame()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("stream-debug-world");
            var scene = world.CreateScene("battle");
            var tracked = scene.CreateEntity()
                .Add(new PositionComponent(0, 0));
            runtime.RegisterModule(new FrameMutationModule(scene, tracked));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync(
                CreateStreamPath(world, scene),
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);

            var initial = await ReadSseDataAsync<GameDebugSnapshotMessage>(reader, timeout.Token);
            Assert.Equal("initial", initial.Frame.Source);
            Assert.Equal(1, FindSceneSnapshot(initial.Snapshot, world, scene).EntityCount);

            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            var pushed = await ReadSseDataAsync<GameDebugSnapshotMessage>(reader, timeout.Token);
            var pushedScene = FindSceneSnapshot(pushed.Snapshot, world, scene);
            var positionStore = Assert.Single(
                pushedScene.ComponentStores,
                store => store.Type.Name == nameof(PositionComponent));

            Assert.Equal(nameof(RuntimeContext), pushed.Frame.Source);
            Assert.Equal(1, pushed.Frame.Frame);
            Assert.True(pushed.Frame.Sequence > initial.Frame.Sequence);
            Assert.Equal(2, pushedScene.EntityCount);
            Assert.Equal(2, positionStore.Count);
            Assert.False(pushed.Control.IsPaused);
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task AutoStart_returns_null_when_environment_is_disabled()
    {
        await GameDebugHostAutoStart.StopAsync();

        using var enabled = EnvironmentVariableScope.Set(GameDebugHostAutoStart.EnabledVariable, "0");
        using var url = EnvironmentVariableScope.Set(GameDebugHostAutoStart.UrlVariable, "http://127.0.0.1:0");

        var host = await GameDebugHostAutoStart.TryStartFromEnvironmentAsync();

        Assert.Null(host);
        Assert.Null(GameDebugHostAutoStart.BaseAddress);
    }

    [Fact]
    public async Task AutoStart_starts_once_from_environment()
    {
        await GameDebugHostAutoStart.StopAsync();

        using var enabled = EnvironmentVariableScope.Set(GameDebugHostAutoStart.EnabledVariable, "1");
        using var url = EnvironmentVariableScope.Set(GameDebugHostAutoStart.UrlVariable, "http://127.0.0.1:0");

        try
        {
            var first = await GameDebugHostAutoStart.TryStartFromEnvironmentAsync();
            var second = await GameDebugHostAutoStart.TryStartFromEnvironmentAsync();

            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.Equal(first.BaseAddress, GameDebugHostAutoStart.BaseAddress);

            using var client = new HttpClient
            {
                BaseAddress = first.BaseAddress,
            };
            var health = await client.GetStringAsync("/_nkg/debug/health");
            Assert.Contains("\"status\":\"ok\"", health, StringComparison.Ordinal);
        }
        finally
        {
            await GameDebugHostAutoStart.StopAsync();
        }
    }

    private readonly record struct PositionComponent(double X, double Y) : IComponent;

    private sealed class FrameMutationModule : Module, IUpdateModule
    {
        private readonly Scene _scene;
        private readonly Entity _tracked;

        public FrameMutationModule(Scene scene, Entity tracked)
        {
            _scene = scene;
            _tracked = tracked;
        }

        public void Update(in GameFrameTime time)
        {
            _tracked.Add(new PositionComponent(time.Frame, time.Frame * 10));
            _scene.CreateEntity()
                .Add(new PositionComponent(time.Frame, time.Frame));
        }
    }

    private sealed class CountingUpdateModule : Module, IUpdateModule
    {
        public int UpdateCount { get; private set; }

        public void Update(in GameFrameTime time)
        {
            UpdateCount++;
        }
    }

    private static string CreateSnapshotPath(
        World world,
        Scene scene,
        bool includePayload,
        bool includeStructured,
        int? entityId = null)
    {
        return CreateDebugPath("snapshot", world, scene, includePayload, includeStructured, entityId);
    }

    private static string CreateStreamPath(World world, Scene scene)
    {
        return CreateDebugPath("stream", world, scene, includePayload: false, includeStructured: false);
    }

    private static string CreateDebugPath(
        string endpoint,
        World world,
        Scene scene,
        bool includePayload,
        bool includeStructured,
        int? entityId = null)
    {
        var query = new List<string>
        {
            $"worldName={Uri.EscapeDataString(world.Name)}",
            $"sceneName={Uri.EscapeDataString(scene.Name)}",
            $"includePayload={includePayload.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}",
            $"includeStructured={includeStructured.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}",
        };

        if (entityId is { } id)
        {
            query.Add($"entityId={id.ToString(CultureInfo.InvariantCulture)}");
        }

        return $"/_nkg/debug/{endpoint}?{string.Join("&", query)}";
    }

    private static SceneDebugSnapshot FindSceneSnapshot(
        GameDebugSnapshot? snapshot,
        World world,
        Scene scene)
    {
        Assert.NotNull(snapshot);
        var worldSnapshot = Assert.Single(
            snapshot.Worlds,
            candidate => candidate.Name == world.Name);
        return Assert.Single(
            worldSnapshot.Scenes,
            candidate => candidate.Name == scene.Name);
    }

    private static ComponentValueDebugNode FindChild(ComponentValueDebugNode node, string name)
    {
        return Assert.Single(node.Children, child => child.Name == name);
    }

    private static async Task<T> ReadSseDataAsync<T>(StreamReader reader, CancellationToken cancellationToken)
    {
        var data = string.Empty;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new EndOfStreamException("The debug stream ended before an SSE data event was received.");
            }

            if (line.Length == 0)
            {
                if (data.Length == 0)
                {
                    continue;
                }

                return JsonSerializer.Deserialize<T>(data, JsonOptions)
                    ?? throw new JsonException($"Could not deserialize SSE data as '{typeof(T).Name}'.");
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data += line["data: ".Length..];
            }
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        private EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static EnvironmentVariableScope Set(string name, string? value)
        {
            return new EnvironmentVariableScope(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
