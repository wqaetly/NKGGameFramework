using NKGGameFramework.Core;

namespace NKGGameFramework.Diagnostics;

public sealed record GameDebugFrameMetrics(
    double DeltaSeconds,
    double RealDeltaSeconds,
    double LogicMilliseconds,
    double LogicFramesPerSecond);

public sealed record GameDebugFrameInfo(
    long Sequence,
    string Source,
    long Frame,
    DateTimeOffset CapturedAt,
    GameDebugFrameMetrics? Metrics = null);

public sealed class GameDebugFramePublisher
{
    public static GameDebugFramePublisher Shared { get; } = new();

    private long _sequence;
    private GameDebugFrameInfo? _lastPublished;

    public event Action<GameDebugFrameInfo>? FrameEnding;

    public event Action<GameDebugFrameInfo>? FramePublished;

    internal void Publish(string source, long frame)
    {
        var info = new GameDebugFrameInfo(
            Interlocked.Increment(ref _sequence),
            source,
            frame,
            DateTimeOffset.UtcNow);
        Publish(info);
    }

    internal void Publish(string source, in GameFrameTime time, TimeSpan logicElapsed)
    {
        var info = new GameDebugFrameInfo(
            Interlocked.Increment(ref _sequence),
            source,
            time.Frame,
            DateTimeOffset.UtcNow,
            CreateMetrics(in time, logicElapsed));
        Publish(info);
    }

    private void Publish(GameDebugFrameInfo info)
    {
        _lastPublished = info;

        var endingHandlers = FrameEnding;
        var publishedHandlers = FramePublished;
        if (endingHandlers is null && publishedHandlers is null)
        {
            return;
        }

        InvokeHandlers(endingHandlers, info);
        InvokeHandlers(publishedHandlers, info);
    }

    private static GameDebugFrameMetrics CreateMetrics(in GameFrameTime time, TimeSpan logicElapsed)
    {
        var logicMilliseconds = logicElapsed.TotalMilliseconds;
        return new GameDebugFrameMetrics(
            time.DeltaSeconds,
            time.RealDeltaSeconds,
            logicMilliseconds,
            logicMilliseconds > 0 ? 1000d / logicMilliseconds : 0d);
    }

    public GameDebugFrameInfo? GetLastPublished()
    {
        return _lastPublished;
    }

    private static void InvokeHandlers(Action<GameDebugFrameInfo>? handlers, GameDebugFrameInfo info)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action<GameDebugFrameInfo> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(info);
            }
            catch
            {
                // Debug subscribers must never break the game frame.
            }
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _sequence, 0);
        _lastPublished = null;
    }
}
