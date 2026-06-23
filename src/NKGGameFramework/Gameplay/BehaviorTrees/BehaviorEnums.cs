namespace NKGGameFramework.Gameplay;

public enum BehaviorTreeStatus
{
    Idle,
    Running,
    Succeeded,
    Failed,
    Canceled,
}

public enum BehaviorNodeState
{
    Inactive,
    Active,
    StopRequested,
}

public enum BehaviorActionStatus
{
    Success,
    Failure,
    Running,
    Blocked,
}

public enum BehaviorActionRequest
{
    Start,
    Update,
    Cancel,
}

public enum BehaviorObserverStops
{
    None,
    Self,
    LowerPriority,
    Both,
    ImmediateRestart,
    LowerPriorityImmediateRestart,
}

public enum BehaviorBlackboardChangeKind
{
    Added,
    Removed,
    Changed,
}

public enum BehaviorConditionOperator
{
    AlwaysTrue,
    IsSet,
    IsNotSet,
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
}
