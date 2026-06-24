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

    public event Action<GameDebugFrameInfo>? FramePublished;

    internal void Publish(string source, long frame)
    {
        var handlers = FramePublished;
        if (handlers is null)
        {
            return;
        }

        var info = new GameDebugFrameInfo(
            Interlocked.Increment(ref _sequence),
            source,
            frame,
            DateTimeOffset.UtcNow);

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
