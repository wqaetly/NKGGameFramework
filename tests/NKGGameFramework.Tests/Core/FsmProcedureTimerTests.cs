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
