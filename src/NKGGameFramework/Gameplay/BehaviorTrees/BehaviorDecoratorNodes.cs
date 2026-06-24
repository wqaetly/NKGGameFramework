namespace NKGGameFramework.Gameplay;

public abstract class BehaviorObservingDecoratorNode : BehaviorDecoratorNode
{
    private readonly Action<BehaviorBlackboardChange> _observer;
    private bool _isObserving;

    protected BehaviorObservingDecoratorNode(string name, BehaviorObserverStops stopsOnChange, BehaviorNode child)
        : base(name, child)
    {
        StopsOnChange = stopsOnChange;
        _observer = _ => Tree.EnqueueExecution(Evaluate);
    }

    public BehaviorObserverStops StopsOnChange { get; }

    protected override void OnStart()
    {
        if (StopsOnChange != BehaviorObserverStops.None && !_isObserving)
        {
            _isObserving = true;
            StartObserving(_observer);
        }

        if (!IsConditionMet())
        {
            Stopped(false);
            return;
        }

        Child.Start();
    }

    protected override void OnCancel()
    {
        if (Child.IsActive)
        {
            Child.Cancel();
        }
        else
        {
            StopObservingIfNeeded();
            Stopped(false);
        }
    }

    protected override void OnChildStopped(BehaviorNode child, bool succeeded)
    {
        if (StopsOnChange is BehaviorObserverStops.None or BehaviorObserverStops.Self)
        {
            StopObservingIfNeeded();
        }

        Stopped(succeeded);
    }

    protected override void OnParentCompositeStopped(BehaviorCompositeNode composite)
    {
        StopObservingIfNeeded();
        base.OnParentCompositeStopped(composite);
    }

    protected void Evaluate()
    {
        var conditionMet = IsConditionMet();
        if (IsActive && !conditionMet)
        {
            if (StopsOnChange is BehaviorObserverStops.Self or BehaviorObserverStops.Both or BehaviorObserverStops.ImmediateRestart)
            {
                Child.Cancel();
            }

            return;
        }

        if (IsActive || !conditionMet)
        {
            return;
        }

        if (StopsOnChange is not (BehaviorObserverStops.LowerPriority
            or BehaviorObserverStops.Both
            or BehaviorObserverStops.ImmediateRestart
            or BehaviorObserverStops.LowerPriorityImmediateRestart))
        {
            return;
        }

        var parent = Parent;
        BehaviorNode child = this;
        while (parent is not null and not BehaviorCompositeNode)
        {
            child = parent;
            parent = parent.Parent;
        }

        if (parent is not BehaviorCompositeNode composite)
        {
            return;
        }

        var immediateRestart = StopsOnChange is BehaviorObserverStops.ImmediateRestart or BehaviorObserverStops.LowerPriorityImmediateRestart;
        if (immediateRestart)
        {
            StopObservingIfNeeded();
        }

        composite.StopLowerPriorityChildrenForChild(child, immediateRestart);
    }

    protected abstract void StartObserving(Action<BehaviorBlackboardChange> observer);

    protected abstract void StopObserving(Action<BehaviorBlackboardChange> observer);

    protected abstract bool IsConditionMet();

    private void StopObservingIfNeeded()
    {
        if (!_isObserving)
        {
            return;
        }

        _isObserving = false;
        StopObserving(_observer);
    }
}

public sealed class BehaviorBlackboardConditionNode : BehaviorObservingDecoratorNode
{
    private readonly BehaviorBlackboardValue _value;

    public BehaviorBlackboardConditionNode(
        string key,
        BehaviorConditionOperator conditionOperator,
        BehaviorBlackboardValue value,
        BehaviorObserverStops stopsOnChange,
        BehaviorNode child)
        : base("BlackboardCondition", stopsOnChange, child)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        Key = key;
        Operator = conditionOperator;
        _value = value;
    }

    public string Key { get; }

    public BehaviorConditionOperator Operator { get; }

    public BehaviorBlackboardValue Value => _value;

    protected override void StartObserving(Action<BehaviorBlackboardChange> observer)
    {
        Blackboard.AddObserver(Key, observer);
    }

    protected override void StopObserving(Action<BehaviorBlackboardChange> observer)
    {
        Blackboard.RemoveObserver(Key, observer);
    }

    protected override bool IsConditionMet()
    {
        if (Operator == BehaviorConditionOperator.AlwaysTrue)
        {
            return true;
        }

        var isSet = Blackboard.TryGetValue(Key, out var blackboardValue);
        if (Operator == BehaviorConditionOperator.IsSet)
        {
            return isSet;
        }

        if (Operator == BehaviorConditionOperator.IsNotSet)
        {
            return !isSet;
        }

        if (!isSet)
        {
            return false;
        }

        return Compare(blackboardValue, _value, Operator);
    }

    private static bool Compare(
        BehaviorBlackboardValue left,
        BehaviorBlackboardValue right,
        BehaviorConditionOperator conditionOperator)
    {
        if (conditionOperator == BehaviorConditionOperator.Equal)
        {
            return left.ValueEquals(right);
        }

        if (conditionOperator == BehaviorConditionOperator.NotEqual)
        {
            return !left.ValueEquals(right);
        }

        if (!BehaviorBlackboardValue.TryCompare(left, right, out var comparison))
        {
            return false;
        }

        return conditionOperator switch
        {
            BehaviorConditionOperator.Greater => comparison > 0,
            BehaviorConditionOperator.GreaterOrEqual => comparison >= 0,
            BehaviorConditionOperator.Less => comparison < 0,
            BehaviorConditionOperator.LessOrEqual => comparison <= 0,
            _ => false,
        };
    }
}
