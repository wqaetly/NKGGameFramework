using NKGGameFramework.Core;

namespace NKGGameFramework.Core.Tests;

public sealed class RuntimeContextTests
{
    [Fact]
    public void RegisterGetUpdateAndShutdownModules()
    {
        using var context = new RuntimeContext();
        var calls = new List<string>();

        var low = context.RegisterModule(new LowUpdateModule(calls));
        var high = context.RegisterModule(new HighUpdateModule(calls));
        var passive = context.RegisterModule(new PassiveModule(calls));

        Assert.Same(high, context.GetModule<HighUpdateModule>());

        context.Update(0.016, 0.017);
        context.Shutdown();

        Assert.True(low.IsInitialized is false);
        Assert.True(high.IsInitialized is false);
        Assert.True(passive.IsInitialized is false);
        Assert.Equal(["init:low", "init:high", "init:passive", "update:high", "update:low", "shutdown:passive", "shutdown:low", "shutdown:high"], calls);
    }

    [Fact]
    public void GetModuleCanResolveByImplementedInterfaceWhenUnambiguous()
    {
        using var context = new RuntimeContext();

        var module = context.RegisterModule(new GameplayModule());

        Assert.Same(module, context.GetModule<IGameplayModule>());
    }

    [Fact]
    public void UpdatePassesDriverFrameTimeToModules()
    {
        using var context = new RuntimeContext();
        var module = context.RegisterModule(new FrameRecordingModule());
        var time = new GameFrameTime(
            42,
            TimeSpan.FromMilliseconds(16),
            TimeSpan.FromMilliseconds(20));

        context.Update(in time);

        Assert.Equal(time, context.Time);
        Assert.Equal(time, module.LastTime);
    }

    private interface IGameplayModule
    {
    }

    private sealed class GameplayModule : Module, IGameplayModule
    {
    }

    private sealed class FrameRecordingModule : Module, IUpdateModule
    {
        public GameFrameTime LastTime { get; private set; }

        public void Update(in GameFrameTime time)
        {
            LastTime = time;
        }
    }

    private sealed class PassiveModule(List<string> calls) : Module
    {
        protected override void OnInitialize(IRuntimeContext context)
        {
            calls.Add("init:passive");
        }

        protected override void OnShutdown()
        {
            calls.Add("shutdown:passive");
        }
    }

    private sealed class LowUpdateModule(List<string> calls) : RecordingUpdateModule("low", 10, calls)
    {
    }

    private sealed class HighUpdateModule(List<string> calls) : RecordingUpdateModule("high", 100, calls)
    {
    }

    private abstract class RecordingUpdateModule(string name, int priority, List<string> calls) : Module, IUpdateModule
    {
        public override int Priority => priority;

        protected override void OnInitialize(IRuntimeContext context)
        {
            calls.Add($"init:{name}");
        }

        protected override void OnShutdown()
        {
            calls.Add($"shutdown:{name}");
        }

        public void Update(in GameFrameTime time)
        {
            calls.Add($"update:{name}");
        }
    }
}
