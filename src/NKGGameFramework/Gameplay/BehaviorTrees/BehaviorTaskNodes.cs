namespace NKGGameFramework.Gameplay;

public sealed class BehaviorWaitNode : BehaviorNode
{
    private readonly TimeSpan? _duration;
    private readonly string? _blackboardKey;
    private long _timerId;

    public BehaviorWaitNode(TimeSpan duration)
        : base("Wait")
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Wait duration cannot be negative.");
        }

        _duration = duration;
    }

    public BehaviorWaitNode(string blackboardKey)
        : base("Wait")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blackboardKey);
        _blackboardKey = blackboardKey;
    }

    protected override void OnStart()
    {
        var duration = ResolveDuration();
        _timerId = Tree.AddTimer(duration, OnTimer);
    }

    protected override void OnCancel()
    {
        Tree.RemoveTimer(_timerId);
        Stopped(false);
    }

    private TimeSpan ResolveDuration()
    {
        if (_duration is { } duration)
        {
            return duration;
        }

        if (_blackboardKey is null || !Blackboard.TryGet(_blackboardKey, out var value) || value is null)
        {
            return TimeSpan.Zero;
        }

        return value switch
        {
            TimeSpan timeSpan when timeSpan > TimeSpan.Zero => timeSpan,
            double seconds when seconds > 0 => TimeSpan.FromSeconds(seconds),
            float seconds when seconds > 0 => TimeSpan.FromSeconds(seconds),
            int milliseconds when milliseconds > 0 => TimeSpan.FromMilliseconds(milliseconds),
            long milliseconds when milliseconds > 0 => TimeSpan.FromMilliseconds(milliseconds),
            _ => TimeSpan.Zero,
        };
    }

    private void OnTimer()
    {
        Stopped(true);
    }
}

public sealed class BehaviorWaitUntilStoppedNode : BehaviorNode
{
    public BehaviorWaitUntilStoppedNode()
        : base("WaitUntilStopped")
    {
    }

    protected override void OnCancel()
    {
        Stopped(false);
    }
}

public sealed class BehaviorActionNode : BehaviorNode, IBehaviorUpdatable
{
    private readonly IBehaviorAction? _action;
    private readonly string? _actionKey;
    private bool _isUpdating;

    public BehaviorActionNode(IBehaviorAction action, IReadOnlyDictionary<string, string>? parameters = null, BuffDefinition? buff = null)
        : base("Action")
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        Parameters = CopyParameters(parameters);
        Buff = buff;
    }

    public BehaviorActionNode(string actionKey, IReadOnlyDictionary<string, string>? parameters = null, BuffDefinition? buff = null)
        : base("Action")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKey);
        _actionKey = actionKey;
        Parameters = CopyParameters(parameters);
        Buff = buff;
    }

    public IReadOnlyDictionary<string, string> Parameters { get; }

    public BuffDefinition? Buff { get; }

    protected override void OnStart()
    {
        HandleResult(ResolveAction().Execute(new BehaviorActionContext(Tree, this, BehaviorActionRequest.Start, Tree.DeltaTime)));
    }

    protected override void OnCancel()
    {
        if (_isUpdating)
        {
            _isUpdating = false;
            Tree.RemoveUpdatable(this);
        }

        var result = ResolveAction().Execute(new BehaviorActionContext(Tree, this, BehaviorActionRequest.Cancel, Tree.DeltaTime));
        Stopped(result == BehaviorActionStatus.Success);
    }

    public void UpdateBehavior()
    {
        if (!_isUpdating || !IsActive)
        {
            return;
        }

        HandleResult(ResolveAction().Execute(new BehaviorActionContext(Tree, this, BehaviorActionRequest.Update, Tree.DeltaTime)));
    }

    private void HandleResult(BehaviorActionStatus status)
    {
        switch (status)
        {
            case BehaviorActionStatus.Success:
                StopUpdating();
                Stopped(true);
                break;
            case BehaviorActionStatus.Failure:
                StopUpdating();
                Stopped(false);
                break;
            case BehaviorActionStatus.Running:
            case BehaviorActionStatus.Blocked:
                if (!_isUpdating)
                {
                    _isUpdating = true;
                    Tree.AddUpdatable(this);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown behavior action status.");
        }
    }

    private void StopUpdating()
    {
        if (_isUpdating)
        {
            _isUpdating = false;
            Tree.RemoveUpdatable(this);
        }
    }

    private IBehaviorAction ResolveAction()
    {
        return _action ?? Tree.Actions.Resolve(_actionKey!);
    }

    private static IReadOnlyDictionary<string, string> CopyParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        return parameters is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(parameters, StringComparer.Ordinal);
    }
}

public sealed class BehaviorServiceNode : BehaviorDecoratorNode
{
    private readonly IBehaviorAction _service;
    private readonly TimeSpan _interval;
    private long _timerId;

    public BehaviorServiceNode(IBehaviorAction service, TimeSpan interval, BehaviorNode child)
        : base("Service", child)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _interval = interval < TimeSpan.Zero ? TimeSpan.Zero : interval;
    }

    protected override void OnStart()
    {
        _timerId = Tree.AddTimer(_interval, TickService, repeat: true);
        TickService();
        base.OnStart();
    }

    protected override void OnChildStopped(BehaviorNode child, bool succeeded)
    {
        Tree.RemoveTimer(_timerId);
        base.OnChildStopped(child, succeeded);
    }

    protected override void OnCancel()
    {
        Tree.RemoveTimer(_timerId);
        base.OnCancel();
    }

    private void TickService()
    {
        _service.Execute(new BehaviorActionContext(Tree, new BehaviorActionNode(_service), BehaviorActionRequest.Update, Tree.DeltaTime));
    }
}

public sealed class BehaviorRepeaterNode : BehaviorDecoratorNode
{
    private readonly int _limit;
    private int _remaining;

    public BehaviorRepeaterNode(BehaviorNode child, int limit = -1)
        : base("Repeater", child)
    {
        if (limit == 0 || limit < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Repeater limit must be -1 or greater than zero.");
        }

        _limit = limit;
    }

    protected override void OnStart()
    {
        _remaining = _limit;
        Child.Start();
    }

    protected override void OnChildStopped(BehaviorNode child, bool succeeded)
    {
        if (IsStopRequested)
        {
            Stopped(succeeded);
            return;
        }

        if (_remaining > 0)
        {
            _remaining--;
            if (_remaining == 0)
            {
                Stopped(true);
                return;
            }
        }

        Child.Start();
    }
}
