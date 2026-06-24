using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Tests.Core;

public sealed class GameDebugControllerTests
{
    [Fact]
    public void Step_only_allows_queued_frame_advances_while_paused()
    {
        var control = new GameDebugController();

        Assert.True(control.TryConsumeFrameAdvance());

        var pause = control.Execute(new GameDebugControlRequest("pause"));
        Assert.True(pause.Succeeded);
        Assert.True(pause.State.IsPaused);
        Assert.False(control.TryConsumeFrameAdvance());

        var step = control.Execute(new GameDebugControlRequest("step", StepCount: 2));
        Assert.True(step.Succeeded);
        Assert.Equal(2, step.State.PendingStepCount);
        Assert.True(control.TryConsumeFrameAdvance());
        Assert.True(control.TryConsumeFrameAdvance());
        Assert.False(control.TryConsumeFrameAdvance());

        var play = control.Execute(new GameDebugControlRequest("play"));
        Assert.True(play.Succeeded);
        Assert.False(play.State.IsPaused);
        Assert.True(control.TryConsumeFrameAdvance());
    }
}
