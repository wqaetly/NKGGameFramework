using System.Net.Http.Json;
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

            var snapshot = await client.GetFromJsonAsync<GameDebugSnapshot>(
                "/_nkg/debug/snapshot",
                JsonOptions);

            Assert.NotNull(snapshot);
            Assert.Contains(snapshot.Runtimes, runtimeSnapshot => runtimeSnapshot.IsDisposed is false);
            var worldSnapshot = Assert.Single(
                snapshot.Worlds,
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
