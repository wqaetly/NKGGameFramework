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
    public void Runtime_context_step_from_playing_mode_pauses_after_one_frame()
    {
        GameDebugController.Shared.Reset();

        try
        {
            using var context = new RuntimeContext();
            var module = context.RegisterModule(new CountingModule());

            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));
            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, context.Time.Frame);

            var step = GameDebugController.Shared.Execute(new GameDebugControlRequest("step"));
            Assert.True(step.State.IsPaused);
            Assert.Equal(1, step.State.PendingStepCount);

            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));
            Assert.Equal(2, module.UpdateCount);
            Assert.Equal(2, context.Time.Frame);

            var consumed = GameDebugController.Shared.GetState();
            Assert.True(consumed.IsPaused);
            Assert.Equal(0, consumed.PendingStepCount);
            Assert.Equal("step-consumed", consumed.LastCommand);

            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 3));
            Assert.Equal(2, module.UpdateCount);
            Assert.Equal(2, context.Time.Frame);
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

    [Fact]
    public void Runtime_context_runs_frame_ending_callbacks_before_published_callbacks()
    {
        GameDebugFramePublisher.Shared.Reset();
        var value = 0;
        var publishedValue = 0;

        void OnFrameEnding(GameDebugFrameInfo _)
        {
            value = 1;
        }

        void OnFramePublished(GameDebugFrameInfo _)
        {
            publishedValue = value;
        }

        GameDebugFramePublisher.Shared.FrameEnding += OnFrameEnding;
        GameDebugFramePublisher.Shared.FramePublished += OnFramePublished;
        try
        {
            using var context = new RuntimeContext();

            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            Assert.Equal(1, publishedValue);
        }
        finally
        {
            GameDebugFramePublisher.Shared.FrameEnding -= OnFrameEnding;
            GameDebugFramePublisher.Shared.FramePublished -= OnFramePublished;
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public void Runtime_context_debug_metrics_sample_logic_update_elapsed_time()
    {
        GameDebugFramePublisher.Shared.Reset();
        GameDebugFrameInfo? published = null;

        void OnFramePublished(GameDebugFrameInfo info)
        {
            published = info;
        }

        GameDebugFramePublisher.Shared.FramePublished += OnFramePublished;
        try
        {
            using var context = new RuntimeContext();
            context.RegisterModule(new BlockingModule(TimeSpan.FromMilliseconds(20)));

            context.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            Assert.NotNull(published);
            var info = published!;
            Assert.NotNull(info.Metrics);
            var metrics = info.Metrics!;
            Assert.Equal(0.016, metrics.DeltaSeconds, precision: 6);
            Assert.Equal(0.016, metrics.RealDeltaSeconds, precision: 6);
            Assert.True(metrics.LogicMilliseconds >= 10d);
            Assert.True(metrics.LogicFramesPerSecond > 0d);
        }
        finally
        {
            GameDebugFramePublisher.Shared.FramePublished -= OnFramePublished;
            GameDebugFramePublisher.Shared.Reset();
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

    private sealed class BlockingModule(TimeSpan delay) : Module, IUpdateModule
    {
        public void Update(in GameFrameTime time)
        {
            Thread.Sleep(delay);
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
