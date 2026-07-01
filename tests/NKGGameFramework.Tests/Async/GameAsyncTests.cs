using NKGGameFramework.Async;
using NKGGameFramework.Core;

namespace NKGGameFramework.Tests.Async;

public sealed class GameAsyncTests
{
    [Fact]
    public async Task FromExceptionPreservesException()
    {
        var exception = new InvalidOperationException("boom");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await GameAsync.FromException(exception));
        var genericThrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await GameAsync.FromException<int>(exception));

        Assert.Same(exception, thrown);
        Assert.Same(exception, genericThrown);
    }

    [Fact]
    public async Task FromCanceledPreservesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(async () => await GameAsync.FromCanceled(cts.Token));
        var genericThrown = await Assert.ThrowsAsync<OperationCanceledException>(async () => await GameAsync.FromCanceled<int>(cts.Token));

        Assert.Equal(cts.Token, thrown.CancellationToken);
        Assert.Equal(cts.Token, genericThrown.CancellationToken);
    }

    [Fact]
    public async Task WhenAnyReturnsFirstCompletedTaskIndex()
    {
        using var context = new RuntimeContext();
        var first = GameAsync.Delay(context.Timers, TimeSpan.FromSeconds(2));
        var second = GameAsync.Delay(context.Timers, TimeSpan.FromSeconds(1));
        var winner = GameAsync.WhenAny(first, second);

        context.Update(1, 1);

        Assert.Equal(1, await winner);

        context.Timers.Clear();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await first);
    }

    [Fact]
    public async Task DelayFrameCompletesAfterRequestedRuntimeFrames()
    {
        using var context = new RuntimeContext();
        var completed = false;
        var delay = AwaitDelayFrameAsync();

        context.Update(new GameFrameTime(1, TimeSpan.Zero, TimeSpan.Zero));
        Assert.False(completed);

        context.Update(new GameFrameTime(2, TimeSpan.Zero, TimeSpan.Zero));

        await delay;
        Assert.True(completed);

        async Task AwaitDelayFrameAsync()
        {
            await GameAsync.DelayFrame(context.Timers, 2);
            completed = true;
        }
    }
}
