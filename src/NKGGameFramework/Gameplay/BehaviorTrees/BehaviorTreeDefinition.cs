namespace NKGGameFramework.Gameplay;

public sealed class BehaviorTreeDefinition
{
    public required BehaviorNodeDefinition Root { get; init; }

    public bool Loop { get; init; }

    public BehaviorTreeInstance CreateInstance(
        BehaviorActionRegistry actions,
        BehaviorTreeContext? context = null,
        BehaviorBlackboard? blackboard = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return new BehaviorTreeInstance(Root.CreateNode(), actions, context, blackboard, Loop);
    }

    public bool TryValidate(BehaviorActionRegistry actions, out string? missingActionKey)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return Root.TryValidate(actions, out missingActionKey);
    }
}

public sealed class BehaviorNodeDefinition
{
    public string Type { get; init; } = BehaviorNodeTypes.Action;

    public List<BehaviorNodeDefinition> Children { get; init; } = [];

    public string? ActionKey { get; init; }

    public Dictionary<string, string> Parameters { get; init; } = [];

    public BuffDefinition? Buff { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? BlackboardKey { get; init; }

    public BehaviorBlackboardValue? Value { get; init; }

    public BehaviorConditionOperator Operator { get; init; } = BehaviorConditionOperator.AlwaysTrue;

    public BehaviorObserverStops StopsOnChange { get; init; } = BehaviorObserverStops.None;

    public int RepeatLimit { get; init; } = -1;

    public bool TryValidate(BehaviorActionRegistry actions, out string? missingActionKey)
    {
        ArgumentNullException.ThrowIfNull(actions);

        if (Type == BehaviorNodeTypes.Action)
        {
            if (string.IsNullOrWhiteSpace(ActionKey))
            {
                missingActionKey = "<empty>";
                return false;
            }

            if (!actions.TryResolve(ActionKey, out _))
            {
                missingActionKey = ActionKey;
                return false;
            }
        }

        foreach (var child in Children)
        {
            if (!child.TryValidate(actions, out missingActionKey))
            {
                return false;
            }
        }

        missingActionKey = null;
        return true;
    }

    public BehaviorNode CreateNode()
    {
        return Type switch
        {
            BehaviorNodeTypes.Sequence => new BehaviorSequenceNode(CreateChildren()),
            BehaviorNodeTypes.Selector => new BehaviorSelectorNode(CreateChildren()),
            BehaviorNodeTypes.Parallel => new BehaviorParallelNode(CreateChildren()),
            BehaviorNodeTypes.Wait => BlackboardKey is { Length: > 0 }
                ? new BehaviorWaitNode(BlackboardKey)
                : new BehaviorWaitNode(Duration ?? TimeSpan.Zero),
            BehaviorNodeTypes.WaitUntilStopped => new BehaviorWaitUntilStoppedNode(),
            BehaviorNodeTypes.Action => new BehaviorActionNode(
                ActionKey ?? throw new InvalidOperationException("Action behavior nodes require ActionKey."),
                Parameters,
                Buff),
            BehaviorNodeTypes.BlackboardCondition => new BehaviorBlackboardConditionNode(
                BlackboardKey ?? throw new InvalidOperationException("Blackboard condition behavior nodes require BlackboardKey."),
                Operator,
                Value ?? BehaviorBlackboardValue.Create<object?>(null),
                StopsOnChange,
                CreateSingleChild()),
            BehaviorNodeTypes.Repeater => new BehaviorRepeaterNode(CreateSingleChild(), RepeatLimit),
            _ => throw new InvalidOperationException($"Unknown behavior node type '{Type}'."),
        };
    }

    private BehaviorNode[] CreateChildren()
    {
        return Children.Select(static child => child.CreateNode()).ToArray();
    }

    private BehaviorNode CreateSingleChild()
    {
        if (Children.Count != 1)
        {
            throw new InvalidOperationException($"Behavior node type '{Type}' requires exactly one child.");
        }

        return Children[0].CreateNode();
    }
}

public static class BehaviorNodeTypes
{
    public const string Sequence = "sequence";
    public const string Selector = "selector";
    public const string Parallel = "parallel";
    public const string Wait = "wait";
    public const string WaitUntilStopped = "wait_until_stopped";
    public const string Action = "action";
    public const string BlackboardCondition = "blackboard_condition";
    public const string Repeater = "repeater";
}
