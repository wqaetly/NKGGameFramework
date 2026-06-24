using Cysharp.Threading.Tasks;

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

public enum TimerExceptionPolicy
{
    Throw,
    Continue,
}

public enum TimerTimeMode
{
    GameTime,
    RealTime,
}

public readonly record struct TimerCallbackContext(
    long TimerId,
    long Tick,
    TimeSpan Elapsed,
    TimeSpan RealElapsed,
    GameFrameTime Time,
    TimerTimeMode TimeMode,
    long FireCount);

public interface IGameTimer
{
    int ScheduledTimerCount { get; }

    TimerExceptionPolicy ExceptionPolicy { get; set; }

    Action<Exception, TimerCallbackContext>? ExceptionHandler { get; set; }

    long Schedule(TimeSpan delay, Action callback, bool repeat = false, TimerTimeMode timeMode = TimerTimeMode.GameTime);

    long Schedule(TimeSpan delay, Action<TimerCallbackContext> callback, bool repeat = false, TimerTimeMode timeMode = TimerTimeMode.GameTime);

    long ScheduleRepeating(TimeSpan interval, Action callback, TimerTimeMode timeMode = TimerTimeMode.GameTime);

    long ScheduleRepeating(TimeSpan delay, TimeSpan interval, Action callback, TimerTimeMode timeMode = TimerTimeMode.GameTime);

    bool Cancel(long timerId);

    bool HasTimer(long timerId);

    UniTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    UniTask DelayAsync(TimeSpan delay, TimerTimeMode timeMode, CancellationToken cancellationToken = default);

    UniTask NextFrameAsync(CancellationToken cancellationToken = default);

    UniTask DelayFrameAsync(int frameCount, CancellationToken cancellationToken = default);

    void Clear();
}

public class GameTimer : IGameTimer
{
    private readonly object _syncRoot = new();
    private readonly PriorityQueue<TimerEntry, TimerPriority> _gameTimers = new();
    private readonly PriorityQueue<TimerEntry, TimerPriority> _realTimers = new();
    private readonly PriorityQueue<TimerEntry, FrameTimerPriority> _frameTimers = new();
    private readonly Dictionary<long, TimerEntry> _activeTimers = [];
    private long _nextId;

    public int ScheduledTimerCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeTimers.Count;
            }
        }
    }

    public long Tick { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public TimeSpan RealElapsed { get; private set; }

    public GameFrameTime Time { get; private set; } = GameFrameTime.Zero;

    public TimerExceptionPolicy ExceptionPolicy { get; set; } = TimerExceptionPolicy.Throw;

    public Action<Exception, TimerCallbackContext>? ExceptionHandler { get; set; }

    public long Schedule(TimeSpan delay, Action callback, bool repeat = false, TimerTimeMode timeMode = TimerTimeMode.GameTime)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return Schedule(
            delay,
            _ => callback(),
            repeat,
            timeMode);
    }

    public long Schedule(TimeSpan delay, Action<TimerCallbackContext> callback, bool repeat = false, TimerTimeMode timeMode = TimerTimeMode.GameTime)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfNegative(delay, nameof(delay));
        ThrowIfInvalid(timeMode, nameof(timeMode));

        return ScheduleCore(delay, delay, repeat, timeMode, callback, onCanceled: null);
    }

    public long ScheduleRepeating(TimeSpan interval, Action callback, TimerTimeMode timeMode = TimerTimeMode.GameTime)
    {
        return ScheduleRepeating(interval, interval, callback, timeMode);
    }

    public long ScheduleRepeating(TimeSpan delay, TimeSpan interval, Action callback, TimerTimeMode timeMode = TimerTimeMode.GameTime)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfNegative(delay, nameof(delay));
        ThrowIfNegative(interval, nameof(interval));
        ThrowIfInvalid(timeMode, nameof(timeMode));

        return ScheduleCore(
            delay,
            interval,
            repeat: true,
            timeMode,
            _ => callback(),
            onCanceled: null);
    }

    public bool Cancel(long timerId)
    {
        TimerEntry? timer;
        lock (_syncRoot)
        {
            if (!_activeTimers.Remove(timerId, out timer))
            {
                return false;
            }

            timer.IsCanceled = true;
        }

        timer.Cancel();
        return true;
    }

    public bool HasTimer(long timerId)
    {
        lock (_syncRoot)
        {
            return _activeTimers.ContainsKey(timerId);
        }
    }

    public UniTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return DelayAsync(delay, TimerTimeMode.GameTime, cancellationToken);
    }

    public UniTask DelayAsync(TimeSpan delay, TimerTimeMode timeMode, CancellationToken cancellationToken = default)
    {
        ThrowIfNegative(delay, nameof(delay));
        ThrowIfInvalid(timeMode, nameof(timeMode));

        if (cancellationToken.IsCancellationRequested)
        {
            return UniTask.FromCanceled(cancellationToken);
        }

        var state = new TimerDelayState(this, cancellationToken);
        var timerId = ScheduleCore(
            delay,
            delay,
            repeat: false,
            timeMode,
            _ => state.Complete(),
            state.Cancel);

        state.Initialize(timerId);
        return state.Task;
    }

    public UniTask NextFrameAsync(CancellationToken cancellationToken = default)
    {
        return DelayFrameAsync(1, cancellationToken);
    }

    public UniTask DelayFrameAsync(int frameCount, CancellationToken cancellationToken = default)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return UniTask.FromCanceled(cancellationToken);
        }

        var state = new TimerDelayState(this, cancellationToken);
        var timerId = ScheduleFrameCore(
            frameCount,
            _ => state.Complete(),
            state.Cancel);

        state.Initialize(timerId);
        return state.Task;
    }

    internal void Advance(in GameFrameTime time)
    {
        List<TimerEntry> dueTimers = [];
        lock (_syncRoot)
        {
            Time = time;
            Tick = time.Frame;
            Elapsed += time.DeltaTime;
            RealElapsed += time.RealDeltaTime;

            CollectDueTimers(_gameTimers, Elapsed, dueTimers);
            CollectDueTimers(_realTimers, RealElapsed, dueTimers);
            CollectDueFrameTimers(dueTimers);
        }

        dueTimers.Sort(static (left, right) => left.Id.CompareTo(right.Id));

        foreach (var timer in dueTimers)
        {
            Invoke(timer);
        }
    }

    public void Clear()
    {
        TimerEntry[] timers;
        lock (_syncRoot)
        {
            timers = [.. _activeTimers.Values];
            foreach (var timer in timers)
            {
                timer.IsCanceled = true;
            }

            _activeTimers.Clear();
            _gameTimers.Clear();
            _realTimers.Clear();
            _frameTimers.Clear();
        }

        foreach (var timer in timers)
        {
            timer.Cancel();
        }
    }

    private long ScheduleFrameCore(
        int frameCount,
        Action<TimerCallbackContext> callback,
        Action? onCanceled)
    {
        lock (_syncRoot)
        {
            var id = checked(++_nextId);
            var dueFrame = checked(Tick + frameCount);
            var timer = new TimerEntry(
                id,
                TimeSpan.Zero,
                TimeSpan.Zero,
                repeat: false,
                TimerTimeMode.GameTime,
                callback,
                onCanceled,
                TimerScheduleKind.Frame,
                dueFrame);

            _activeTimers.Add(id, timer);
            Enqueue(timer);
            return id;
        }
    }

    private long ScheduleCore(
        TimeSpan delay,
        TimeSpan interval,
        bool repeat,
        TimerTimeMode timeMode,
        Action<TimerCallbackContext> callback,
        Action? onCanceled)
    {
        lock (_syncRoot)
        {
            var id = checked(++_nextId);
            var dueTime = GetElapsed(timeMode) + delay;
            var timer = new TimerEntry(id, dueTime, interval, repeat, timeMode, callback, onCanceled);

            _activeTimers.Add(id, timer);
            Enqueue(timer);
            return id;
        }
    }

    private void CollectDueTimers(
        PriorityQueue<TimerEntry, TimerPriority> queue,
        TimeSpan elapsed,
        List<TimerEntry> dueTimers)
    {
        while (queue.TryPeek(out var timer, out var priority) && priority.DueTime <= elapsed)
        {
            queue.Dequeue();
            if (timer.IsCanceled ||
                !_activeTimers.TryGetValue(timer.Id, out var activeTimer) ||
                !ReferenceEquals(activeTimer, timer))
            {
                continue;
            }

            dueTimers.Add(timer);
        }
    }

    private void CollectDueFrameTimers(List<TimerEntry> dueTimers)
    {
        while (_frameTimers.TryPeek(out var timer, out var priority) && priority.DueFrame <= Tick)
        {
            _frameTimers.Dequeue();
            if (timer.IsCanceled ||
                !_activeTimers.TryGetValue(timer.Id, out var activeTimer) ||
                !ReferenceEquals(activeTimer, timer))
            {
                continue;
            }

            dueTimers.Add(timer);
        }
    }

    private void Invoke(TimerEntry timer)
    {
        if (!TryBeginInvoke(timer, out var context))
        {
            return;
        }

        var completed = false;
        try
        {
            timer.Callback(context);
            completed = true;
        }
        catch (Exception ex) when (ExceptionPolicy == TimerExceptionPolicy.Continue)
        {
            ExceptionHandler?.Invoke(ex, context);
            completed = true;
        }
        finally
        {
            if (timer.Repeat)
            {
                if (completed)
                {
                    Reschedule(timer);
                }
                else
                {
                    RemoveActive(timer);
                }
            }
        }
    }

    private bool TryBeginInvoke(TimerEntry timer, out TimerCallbackContext context)
    {
        lock (_syncRoot)
        {
            if (timer.IsCanceled ||
                !_activeTimers.TryGetValue(timer.Id, out var activeTimer) ||
                !ReferenceEquals(activeTimer, timer))
            {
                context = default;
                return false;
            }

            timer.FireCount++;
            if (!timer.Repeat)
            {
                _activeTimers.Remove(timer.Id);
            }

            context = new TimerCallbackContext(
                timer.Id,
                Tick,
                Elapsed,
                RealElapsed,
                Time,
                timer.TimeMode,
                timer.FireCount);
            return true;
        }
    }

    private void Reschedule(TimerEntry timer)
    {
        lock (_syncRoot)
        {
            if (timer.IsCanceled ||
                !_activeTimers.TryGetValue(timer.Id, out var activeTimer) ||
                !ReferenceEquals(activeTimer, timer))
            {
                return;
            }

            timer.DueTime = GetElapsed(timer.TimeMode) + timer.Interval;
            Enqueue(timer);
        }
    }

    private void RemoveActive(TimerEntry timer)
    {
        lock (_syncRoot)
        {
            if (_activeTimers.TryGetValue(timer.Id, out var activeTimer) &&
                ReferenceEquals(activeTimer, timer))
            {
                timer.IsCanceled = true;
                _activeTimers.Remove(timer.Id);
            }
        }
    }

    private void Enqueue(TimerEntry timer)
    {
        if (timer.Kind == TimerScheduleKind.Frame)
        {
            _frameTimers.Enqueue(timer, new FrameTimerPriority(timer.DueFrame, timer.Id));
            return;
        }

        GetQueue(timer.TimeMode).Enqueue(timer, new TimerPriority(timer.DueTime, timer.Id));
    }

    private TimeSpan GetElapsed(TimerTimeMode timeMode)
    {
        return timeMode switch
        {
            TimerTimeMode.GameTime => Elapsed,
            TimerTimeMode.RealTime => RealElapsed,
            _ => throw new ArgumentOutOfRangeException(nameof(timeMode)),
        };
    }

    private PriorityQueue<TimerEntry, TimerPriority> GetQueue(TimerTimeMode timeMode)
    {
        return timeMode switch
        {
            TimerTimeMode.GameTime => _gameTimers,
            TimerTimeMode.RealTime => _realTimers,
            _ => throw new ArgumentOutOfRangeException(nameof(timeMode)),
        };
    }

    private static void ThrowIfNegative(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ThrowIfInvalid(TimerTimeMode timeMode, string parameterName)
    {
        if (!Enum.IsDefined(timeMode))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private enum TimerScheduleKind
    {
        Time,
        Frame,
    }

    private sealed class TimerEntry(
        long id,
        TimeSpan dueTime,
        TimeSpan interval,
        bool repeat,
        TimerTimeMode timeMode,
        Action<TimerCallbackContext> callback,
        Action? onCanceled,
        TimerScheduleKind kind = TimerScheduleKind.Time,
        long dueFrame = 0)
    {
        public long Id { get; } = id;

        public TimeSpan DueTime { get; set; } = dueTime;

        public TimeSpan Interval { get; } = interval;

        public bool Repeat { get; } = repeat;

        public TimerTimeMode TimeMode { get; } = timeMode;

        public TimerScheduleKind Kind { get; } = kind;

        public long DueFrame { get; } = dueFrame;

        public Action<TimerCallbackContext> Callback { get; } = callback;

        public long FireCount { get; set; }

        public bool IsCanceled { get; set; }

        public void Cancel()
        {
            onCanceled?.Invoke();
        }
    }

    private sealed class TimerDelayState(GameTimer timer, CancellationToken cancellationToken)
    {
        private readonly UniTaskCompletionSource<AsyncUnit> _completion = new();
        private CancellationTokenRegistration _cancellationRegistration;
        private int _isCompleted;
        private long _timerId;

        public UniTask Task => _completion.Task.AsUniTask();

        public void Initialize(long timerId)
        {
            _timerId = timerId;
            if (!cancellationToken.CanBeCanceled)
            {
                return;
            }

            _cancellationRegistration = cancellationToken.Register(
                static state => ((TimerDelayState)state!).CancelFromToken(),
                this);

            if (Volatile.Read(ref _isCompleted) != 0)
            {
                _cancellationRegistration.Dispose();
            }
        }

        public void Complete()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _cancellationRegistration.Dispose();
            _completion.TrySetResult(AsyncUnit.Default);
        }

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _cancellationRegistration.Dispose();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(cancellationToken);
            }
            else
            {
                _completion.TrySetCanceled();
            }
        }

        private void CancelFromToken()
        {
            timer.Cancel(_timerId);
        }
    }

    private readonly record struct TimerPriority(TimeSpan DueTime, long Id) : IComparable<TimerPriority>
    {
        public int CompareTo(TimerPriority other)
        {
            var dueTimeComparison = DueTime.CompareTo(other.DueTime);
            return dueTimeComparison != 0
                ? dueTimeComparison
                : Id.CompareTo(other.Id);
        }
    }

    private readonly record struct FrameTimerPriority(long DueFrame, long Id) : IComparable<FrameTimerPriority>
    {
        public int CompareTo(FrameTimerPriority other)
        {
            var dueFrameComparison = DueFrame.CompareTo(other.DueFrame);
            return dueFrameComparison != 0
                ? dueFrameComparison
                : Id.CompareTo(other.Id);
        }
    }
}

public sealed class TimerService : GameTimer, IUpdateModule
{
    public void Update(in GameFrameTime time)
    {
        Advance(in time);
    }

    public void Update(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.Advance(Time, deltaTime, realDeltaTime);
        Update(in time);
    }
}
