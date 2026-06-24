namespace NKGGameFramework.Core;

public interface IGameClock
{
    long Tick { get; }

    TimeSpan Elapsed { get; }
}

public sealed class ManualGameClock : IGameClock
{
    public long Tick { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public GameFrameTime AdvanceFrame(TimeSpan delta, TimeSpan? realDelta = null)
    {
        Advance(delta);
        return new GameFrameTime(Tick, delta, realDelta ?? delta);
    }

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        Tick++;
        Elapsed += delta;
    }
}

public sealed class TimerService : IUpdateModule
{
    private readonly PriorityQueue<TimerEntry, TimeSpan> _timers = new();
    private long _nextId;

    public long Tick { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public GameFrameTime Time { get; private set; } = GameFrameTime.Zero;

    public long Schedule(TimeSpan delay, Action callback, bool repeat = false)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        var timer = new TimerEntry(++_nextId, Elapsed + delay, delay, repeat, callback);
        _timers.Enqueue(timer, timer.DueTime);
        return timer.Id;
    }

    public void Update(in GameFrameTime time)
    {
        Time = time;
        Tick = time.Frame;
        Elapsed += time.DeltaTime;

        while (_timers.TryPeek(out var timer, out var dueTime) && dueTime <= Elapsed)
        {
            _timers.Dequeue();
            timer.Callback();

            if (timer.Repeat)
            {
                var next = timer with { DueTime = Elapsed + timer.Interval };
                _timers.Enqueue(next, next.DueTime);
            }
        }
    }

    public void Update(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.Advance(Time, deltaTime, realDeltaTime);
        Update(in time);
    }

    private sealed record TimerEntry(long Id, TimeSpan DueTime, TimeSpan Interval, bool Repeat, Action Callback);
}
