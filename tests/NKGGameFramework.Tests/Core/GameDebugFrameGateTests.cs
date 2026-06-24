using NKGGameFramework.Core;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.Ecs;
using NKGGameFramework.Tests.Hosting;

namespace NKGGameFramework.Tests.Core;

[Collection(GameDebugRegistryCollection.Name)]
public sealed class GameDebugFrameGateTests
{
    [Fact]
    public void Runtime_context_update_is_skipped_while_debug_playback_is_paused()
    {
        GameDebugController.Shared.Reset();

        try
        {
            using var context = new RuntimeContext();
            var module = context.RegisterModule(new CountingModule());

            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));
            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, context.Time.Frame);

            GameDebugController.Shared.Execute(new GameDebugControlRequest("pause"));
            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, context.Time.Frame);

            GameDebugController.Shared.Execute(new GameDebugControlRequest("step"));
            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            Assert.Equal(2, module.UpdateCount);
            Assert.Equal(2, context.Time.Frame);
            Assert.Equal(0, GameDebugController.Shared.GetState().PendingStepCount);

            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 3));
            Assert.Equal(2, module.UpdateCount);
            Assert.Equal(2, context.Time.Frame);

            GameDebugController.Shared.Execute(new GameDebugControlRequest("play"));
            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 3));

            Assert.Equal(3, module.UpdateCount);
            Assert.Equal(3, context.Time.Frame);
        }
        finally
        {
            GameDebugController.Shared.Reset();
        }
    }

    [Fact]
    public void Runtime_context_step_allows_runtime_driven_world_update_once()
    {
        GameDebugController.Shared.Reset();

        try
        {
            using var world = new World("nested-debug-world");
            using var context = new RuntimeContext();
            var scene = world.CreateScene("nested-debug-scene");
            var system = new CountingSystem();
            scene.Systems.Add(system);
            var module = context.RegisterModule(new WorldDrivingModule(world));

            GameDebugController.Shared.Execute(new GameDebugControlRequest("pause"));
            GameDebugController.Shared.Execute(new GameDebugControlRequest("step"));
            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, system.UpdateCount);
            Assert.Equal(1, context.Time.Frame);
            Assert.Equal(1, world.Time.Frame);
            Assert.Equal(1, scene.Time.Frame);
            Assert.Equal(0, GameDebugController.Shared.GetState().PendingStepCount);

            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, system.UpdateCount);
            Assert.Equal(1, context.Time.Frame);
            Assert.Equal(1, world.Time.Frame);
            Assert.Equal(1, scene.Time.Frame);
        }
        finally
        {
            GameDebugController.Shared.Reset();
        }
    }

    private sealed class CountingModule : Module, IUpdateModule
    {
        public int UpdateCount { get; private set; }

        public void Update(in GameFrameTime time)
        {
            UpdateCount++;
        }
    }

    private sealed class WorldDrivingModule(World world) : Module, IUpdateModule
    {
        public int UpdateCount { get; private set; }

        public void Update(in GameFrameTime time)
        {
            UpdateCount++;
            world.Update(in time);
        }
    }

    private sealed class CountingSystem : EcsSystem
    {
        public int UpdateCount { get; private set; }

        public override void Update(Scene scene, in SystemUpdateContext context)
        {
            UpdateCount++;
        }
    }
}
