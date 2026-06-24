namespace NKGGameFramework.Diagnostics;

public sealed record GameDebugFrameInfo(
    long Sequence,
    string Source,
    long Frame,
    DateTimeOffset CapturedAt);

public sealed class GameDebugFramePublisher
{
    public static GameDebugFramePublisher Shared { get; } = new();

    private long _sequence;

    public event Action<GameDebugFrameInfo>? FrameEnding;

    public event Action<GameDebugFrameInfo>? FramePublished;

    internal void Publish(string source, long frame)
    {
        var endingHandlers = FrameEnding;
        var publishedHandlers = FramePublished;
        if (endingHandlers is null && publishedHandlers is null)
        {
            return;
        }

        var info = new GameDebugFrameInfo(
            Interlocked.Increment(ref _sequence),
            source,
            frame,
            DateTimeOffset.UtcNow);

        InvokeHandlers(endingHandlers, info);
        InvokeHandlers(publishedHandlers, info);
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
    }
}
