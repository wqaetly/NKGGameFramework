using NKGGameFramework.Async;
using NKGGameFramework.Core;

namespace NKGGameFramework.Core.Tests;

public sealed class FsmProcedureTimerTests
{
    [Fact]
    public void FsmChangesStateAndTracksStateTime()
    {
        var calls = new List<string>();
        var fsm = new Fsm<TestOwner>("test", new TestOwner(), [new BootState(calls), new PlayState(calls)]);

        fsm.Start<BootState>();
        fsm.Update(0.5, 0.5);
        fsm.ChangeState<PlayState>();

        Assert.Equal(["enter:boot", "update:boot", "leave:boot", "enter:play"], calls);
        Assert.Equal(0, fsm.CurrentStateTime);
    }

    [Fact]
    public void ProcedureModuleUsesFsmSemantics()
    {
        var procedure = new ProcedureModule();

        procedure.Initialize(new BootProcedure(), new LoginProcedure());
        procedure.StartProcedure<BootProcedure>();

        Assert.True(procedure.HasProcedure<LoginProcedure>());
        Assert.IsType<BootProcedure>(procedure.CurrentProcedure);
        Assert.IsType<LoginProcedure>(procedure.GetProcedure<LoginProcedure>());
    }

    [Fact]
    public void RuntimeContextDrivesRegisteredProcedureModule()
    {
        var calls = new List<string>();
        using var context = new RuntimeContext();
        var procedures = context.RegisterModule(new ProcedureModule());

        procedures.Initialize(new BootProcedureWithCalls(calls), new LoginProcedureWithCalls(calls));
        procedures.StartProcedure<BootProcedureWithCalls>();
        context.Update(0.5, 0.5);
        context.Shutdown();

        Assert.Equal(["enter:boot", "update:boot", "leave:boot", "enter:login", "leave:login"], calls);
    }

    [Fact]
    public void TimerServiceRunsDueCallbacks()
    {
        var timer = new TimerService();
        var count = 0;

        timer.Schedule(TimeSpan.FromSeconds(1), () => count++);
        timer.Update(0.5, 0.5);
        timer.Update(0.5, 0.5);

        Assert.Equal(1, count);
    }

    [Fact]
    public void TimerServiceUsesDriverFrameTime()
    {
        var timer = new TimerService();
        var count = 0;

        timer.Schedule(TimeSpan.FromSeconds(1), () => count++);
        var firstFrame = new GameFrameTime(10, TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(0.5));
        var secondFrame = new GameFrameTime(11, TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(0.7));

        timer.Update(in firstFrame);
        timer.Update(in secondFrame);

        Assert.Equal(11, timer.Tick);
        Assert.Equal(TimeSpan.FromSeconds(1), timer.Elapsed);
        Assert.Equal(1, count);
    }

    [Fact]
    public void TimerCanCancelScheduledCallbacks()
    {
        using var context = new RuntimeContext();
        var count = 0;

        var timerId = context.Timers.Schedule(TimeSpan.FromSeconds(1), () => count++);

        Assert.True(context.Timers.HasTimer(timerId));
        Assert.True(context.Timers.Cancel(timerId));
        Assert.False(context.Timers.HasTimer(timerId));

        context.Update(1, 1);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task TimerDelayAsyncCompletesWhenTimerAdvances()
    {
        using var context = new RuntimeContext();
        var completed = false;
        var delay = AwaitDelayAsync();

        context.Update(0.4, 0.4);
        Assert.False(completed);

        context.Update(0.6, 0.6);

        await delay;
        Assert.True(completed);

        async Task AwaitDelayAsync()
        {
            await GameAsync.Delay(context.Timers, TimeSpan.FromSeconds(1));
            completed = true;
        }
    }

    [Fact]
    public async Task TimerDelayAsyncIsCanceledWhenTimerIsCleared()
    {
        using var context = new RuntimeContext();
        var delay = GameAsync.Delay(context.Timers, TimeSpan.FromSeconds(1));

        context.Timers.Clear();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await delay);
    }

    [Fact]
    public async Task TimerNextFrameAsyncCompletesOnNextRuntimeFrame()
    {
        using var context = new RuntimeContext();
        var completed = false;
        var delay = AwaitNextFrameAsync();

        Assert.False(completed);

        context.Update(new GameFrameTime(1, TimeSpan.Zero, TimeSpan.Zero));

        await delay;
        Assert.True(completed);

        async Task AwaitNextFrameAsync()
        {
            await GameAsync.NextFrame(context.Timers);
            completed = true;
        }
    }

    [Fact]
    public void TimerSupportsRealTimeModeDuringGameplayPause()
    {
        using var context = new RuntimeContext();
        var gameCount = 0;
        var realCount = 0;

        context.Timers.Schedule(TimeSpan.FromSeconds(1), () => gameCount++);
        context.Timers.Schedule(TimeSpan.FromSeconds(1), () => realCount++, timeMode: TimerTimeMode.RealTime);
        context.Update(new GameFrameTime(1, TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        Assert.Equal(0, gameCount);
        Assert.Equal(1, realCount);
    }

    [Fact]
    public void RuntimeContextDrivesGlobalTimer()
    {
        using var context = new RuntimeContext();
        var count = 0;

        context.Timers.Schedule(TimeSpan.FromSeconds(1), () => count++);
        context.Update(0.5, 0.5);
        context.Update(0.5, 0.5);

        Assert.Equal(1, count);
    }

    private sealed class TestOwner;

    private sealed class BootState(List<string> calls) : FsmState<TestOwner>
    {
        protected override void OnEnter(Fsm<TestOwner> fsm) => calls.Add("enter:boot");

        protected override void OnUpdate(Fsm<TestOwner> fsm, double deltaTime, double realDeltaTime) => calls.Add("update:boot");

        protected override void OnLeave(Fsm<TestOwner> fsm, bool isShutdown) => calls.Add("leave:boot");
    }

    private sealed class PlayState(List<string> calls) : FsmState<TestOwner>
    {
        protected override void OnEnter(Fsm<TestOwner> fsm) => calls.Add("enter:play");
    }

    private sealed class BootProcedure : ProcedureBase;

    private sealed class LoginProcedure : ProcedureBase;

    private sealed class BootProcedureWithCalls(List<string> calls) : ProcedureBase
    {
        protected override void OnEnter(Fsm<IProcedureModule> fsm) => calls.Add("enter:boot");

        protected override void OnUpdate(Fsm<IProcedureModule> fsm, double deltaTime, double realDeltaTime)
        {
            calls.Add("update:boot");
            ChangeProcedure<LoginProcedureWithCalls>(fsm);
        }

        protected override void OnLeave(Fsm<IProcedureModule> fsm, bool isShutdown) => calls.Add("leave:boot");
    }

    private sealed class LoginProcedureWithCalls(List<string> calls) : ProcedureBase
    {
        protected override void OnEnter(Fsm<IProcedureModule> fsm) => calls.Add("enter:login");

        protected override void OnLeave(Fsm<IProcedureModule> fsm, bool isShutdown) => calls.Add("leave:login");
    }
}
