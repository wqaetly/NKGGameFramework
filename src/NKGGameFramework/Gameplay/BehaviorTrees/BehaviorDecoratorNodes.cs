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
    public BehaviorBlackboardConditionNode(
        string key,
        BehaviorConditionOperator conditionOperator,
        object? value,
        BehaviorObserverStops stopsOnChange,
        BehaviorNode child)
        : base("BlackboardCondition", stopsOnChange, child)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Operator = conditionOperator;
        Value = value;
    }

    public string Key { get; }

    public BehaviorConditionOperator Operator { get; }

    public object? Value { get; }

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

        var isSet = Blackboard.TryGet(Key, out var blackboardValue);
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

        return Compare(blackboardValue, Value, Operator);
    }

    private static bool Compare(object? left, object? right, BehaviorConditionOperator conditionOperator)
    {
        if (conditionOperator == BehaviorConditionOperator.Equal)
        {
            return Equals(left, right);
        }

        if (conditionOperator == BehaviorConditionOperator.NotEqual)
        {
            return !Equals(left, right);
        }

        if (left is null || right is null)
        {
            return false;
        }

        var comparison = CompareValues(left, right);
        return conditionOperator switch
        {
            BehaviorConditionOperator.Greater => comparison > 0,
            BehaviorConditionOperator.GreaterOrEqual => comparison >= 0,
            BehaviorConditionOperator.Less => comparison < 0,
            BehaviorConditionOperator.LessOrEqual => comparison <= 0,
            _ => false,
        };
    }

    private static int CompareValues(object left, object right)
    {
        if (TryConvertToDouble(left, out var leftNumber) && TryConvertToDouble(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left is IComparable comparable && left.GetType().IsInstanceOfType(right))
        {
            return comparable.CompareTo(right);
        }

        return string.CompareOrdinal(left.ToString(), right.ToString());
    }

    private static bool TryConvertToDouble(object value, out double result)
    {
        switch (value)
        {
            case byte typed:
                result = typed;
                return true;
            case sbyte typed:
                result = typed;
                return true;
            case short typed:
                result = typed;
                return true;
            case ushort typed:
                result = typed;
                return true;
            case int typed:
                result = typed;
                return true;
            case uint typed:
                result = typed;
                return true;
            case long typed:
                result = typed;
                return true;
            case ulong typed:
                result = typed;
                return true;
            case float typed:
                result = typed;
                return true;
            case double typed:
                result = typed;
                return true;
            case decimal typed:
                result = (double)typed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
