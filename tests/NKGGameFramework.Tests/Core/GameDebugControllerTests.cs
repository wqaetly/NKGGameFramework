using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Tests.Core;

public sealed class GameDebugControllerTests
{
    [Fact]
    public void Control_commands_update_debug_playback_state()
    {
        var control = new GameDebugController();

        var pause = control.Execute(new GameDebugControlRequest("pause"));
        Assert.True(pause.Succeeded);
        Assert.True(pause.State.IsPaused);
        Assert.Equal(0, pause.State.PendingStepCount);
        Assert.Equal("pause", pause.State.LastCommand);

        var step = control.Execute(new GameDebugControlRequest("step", StepCount: 2));
        Assert.True(step.Succeeded);
        Assert.True(step.State.IsPaused);
        Assert.Equal(2, step.State.PendingStepCount);
        Assert.Equal("step", step.State.LastCommand);

        var play = control.Execute(new GameDebugControlRequest("play"));
        Assert.True(play.Succeeded);
        Assert.False(play.State.IsPaused);
        Assert.Equal(0, play.State.PendingStepCount);
        Assert.Equal("play", play.State.LastCommand);
    }
}
