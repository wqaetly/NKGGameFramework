using NKGGameFramework.Core;

namespace NKGGameFramework.Gameplay;

public sealed class BehaviorTreeInstance : IDisposable
{
    private readonly Queue<Action> _executionRequests = [];
    private readonly List<BehaviorTimer> _timers = [];
    private readonly HashSet<IBehaviorUpdatable> _updatables = [];
    private readonly List<Action> _timerCallbacks = [];
    private readonly List<IBehaviorUpdatable> _updatablesToUpdate = [];
    private readonly bool _ownsBlackboard;
    private bool _isPumping;
    private bool _cancelRequested;
    private bool _disposed;
    private long _nextTimerId;

    public BehaviorTreeInstance(
        BehaviorNode root,
        BehaviorActionRegistry actions,
        BehaviorTreeContext? context = null,
        BehaviorBlackboard? blackboard = null,
        bool loop = false)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        Context = context ?? new BehaviorTreeContext();
        _ownsBlackboard = blackboard is null;
        Blackboard = blackboard ?? CreateDefaultBlackboard(Context);
        Loop = loop;

        Root.AttachTo(this, null);
    }

    public BehaviorNode Root { get; }

    public BehaviorActionRegistry Actions { get; }

    public BehaviorTreeContext Context { get; }

    public BehaviorBlackboard Blackboard { get; }

    public BehaviorTreeStatus Status { get; private set; } = BehaviorTreeStatus.Idle;

    public bool Loop { get; }

    public TimeSpan DeltaTime { get; private set; }

    public GameFrameTime Time { get; private set; } = GameFrameTime.Zero;

    public bool IsComplete => Status is BehaviorTreeStatus.Succeeded or BehaviorTreeStatus.Failed or BehaviorTreeStatus.Canceled;

    public event Action<BehaviorTreeInstance, BehaviorTreeStatus>? Completed;

    public void Start()
    {
        ThrowIfDisposed();

        if (Status == BehaviorTreeStatus.Running)
        {
            return;
        }

        if (Status != BehaviorTreeStatus.Idle)
        {
            throw new InvalidOperationException("Completed behavior tree instances cannot be restarted.");
        }

        Status = BehaviorTreeStatus.Running;
        Root.Start();
        PumpExecutionRequests();
    }

    public void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        if (Status != BehaviorTreeStatus.Running)
        {
            return;
        }

        _cancelRequested = true;
        if (Root.IsActive)
        {
            Root.Cancel();
        }
        else
        {
            Finish(BehaviorTreeStatus.Canceled);
        }

        PumpExecutionRequests();
    }

    public void Update(in GameFrameTime time)
    {
        if (Status != BehaviorTreeStatus.Running)
        {
            return;
        }

        ThrowIfDisposed();

        Time = time;
        DeltaTime = time.DeltaTime;
        TickTimers(time.DeltaTime);
        TickUpdatables();
        PumpExecutionRequests();
    }

    public void Update(TimeSpan deltaTime)
    {
        var time = GameFrameTime.Advance(Time, deltaTime, deltaTime);
        Update(in time);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (Status == BehaviorTreeStatus.Running)
        {
            Cancel();
        }

        if (_disposed)
        {
            return;
        }

        if (Status == BehaviorTreeStatus.Idle)
        {
            Status = BehaviorTreeStatus.Canceled;
        }

        ClearRuntimeState();
        ReleaseOwnedResources();
    }

    internal void EnqueueExecution(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _executionRequests.Enqueue(action);

        if (!_isPumping)
        {
            PumpExecutionRequests();
        }
    }

    internal long AddTimer(TimeSpan delay, Action callback, bool repeat = false)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var id = ++_nextTimerId;
        if (delay <= TimeSpan.Zero)
        {
            EnqueueExecution(callback);
            if (!repeat)
            {
                return id;
            }

            delay = TimeSpan.FromTicks(1);
        }

        _timers.Add(new BehaviorTimer(id, delay, delay, repeat, callback));
        return id;
    }

    internal void RemoveTimer(long id)
    {
        _timers.RemoveAll(timer => timer.Id == id);
    }

    internal void AddUpdatable(IBehaviorUpdatable updatable)
    {
        _updatables.Add(updatable);
    }

    internal void RemoveUpdatable(IBehaviorUpdatable updatable)
    {
        _updatables.Remove(updatable);
    }

    internal void OnNodeStopped(BehaviorNode node, bool success)
    {
        if (node.Parent is not null)
        {
            EnqueueExecution(() => node.Parent.ChildStopped(node, success));
            return;
        }

        if (Loop && !_cancelRequested)
        {
            EnqueueExecution(node.Start);
            return;
        }

        Finish(_cancelRequested ? BehaviorTreeStatus.Canceled : success ? BehaviorTreeStatus.Succeeded : BehaviorTreeStatus.Failed);
    }

    private void PumpExecutionRequests()
    {
        if (_isPumping)
        {
            return;
        }

        _isPumping = true;
        try
        {
            var operations = 0;
            while (_executionRequests.Count > 0 && Status == BehaviorTreeStatus.Running)
            {
                if (++operations > 1_000_000)
                {
                    throw new InvalidOperationException("Behavior tree exceeded the execution request limit. Check for a zero-delay infinite loop.");
                }

                _executionRequests.Dequeue().Invoke();
            }
        }
        finally
        {
            _isPumping = false;
        }
    }

    private void TickTimers(TimeSpan deltaTime)
    {
        if (_timers.Count == 0)
        {
            return;
        }

        _timerCallbacks.Clear();
        for (var index = 0; index < _timers.Count; index++)
        {
            var current = _timers[index];
            current.Remaining -= deltaTime;
            if (current.Remaining > TimeSpan.Zero)
            {
                _timers[index] = current;
                continue;
            }

            if (current.Repeat)
            {
                current.Remaining = current.Interval;
                _timers[index] = current;
            }
            else
            {
                _timers.RemoveAt(index);
                index--;
            }

            _timerCallbacks.Add(current.Callback);
        }

        for (var index = 0; index < _timerCallbacks.Count; index++)
        {
            if (_disposed || Status != BehaviorTreeStatus.Running)
            {
                break;
            }

            var callback = _timerCallbacks[index];
            EnqueueExecution(callback);
        }

        _timerCallbacks.Clear();
    }

    private void TickUpdatables()
    {
        if (_updatables.Count == 0)
        {
            return;
        }

        _updatablesToUpdate.Clear();
        _updatablesToUpdate.AddRange(_updatables);
        for (var index = 0; index < _updatablesToUpdate.Count; index++)
        {
            if (_disposed || Status != BehaviorTreeStatus.Running)
            {
                break;
            }

            var updatable = _updatablesToUpdate[index];
            if (_updatables.Contains(updatable))
            {
                EnqueueExecution(updatable.UpdateBehavior);
            }
        }

        _updatablesToUpdate.Clear();
    }

    private void Finish(BehaviorTreeStatus status)
    {
        if (Status != BehaviorTreeStatus.Running)
        {
            return;
        }

        Status = status;
        ClearRuntimeState();
        Completed?.Invoke(this, status);
        ReleaseOwnedResources();
    }

    private void ClearRuntimeState()
    {
        _timers.Clear();
        _updatables.Clear();
        _timerCallbacks.Clear();
        _updatablesToUpdate.Clear();
        _executionRequests.Clear();
    }

    private void ReleaseOwnedResources()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsBlackboard)
        {
            Blackboard.Dispose();
        }

        Completed = null;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static BehaviorBlackboard CreateDefaultBlackboard(BehaviorTreeContext context)
    {
        if (context.Scene is { } scene)
        {
            return BehaviorBlackboard.CreateForScene(scene);
        }

        throw new InvalidOperationException("Default behavior blackboards require BehaviorTreeContext.Scene.");
    }

    private struct BehaviorTimer
    {
        public BehaviorTimer(long id, TimeSpan interval, TimeSpan remaining, bool repeat, Action callback)
        {
            Id = id;
            Interval = interval;
            Remaining = remaining;
            Repeat = repeat;
            Callback = callback;
        }

        public long Id { get; }

        public TimeSpan Interval { get; }

        public TimeSpan Remaining { get; set; }

        public bool Repeat { get; }

        public Action Callback { get; }
    }
}

internal interface IBehaviorUpdatable
{
    void UpdateBehavior();
}
