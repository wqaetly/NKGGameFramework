namespace NKGGameFramework.Gameplay;

public abstract class BehaviorNode
{
    protected BehaviorNode(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public string? Label { get; init; }

    public BehaviorNodeState State { get; private set; } = BehaviorNodeState.Inactive;

    public bool IsActive => State == BehaviorNodeState.Active;

    public bool IsStopRequested => State == BehaviorNodeState.StopRequested;

    public BehaviorTreeInstance Tree { get; private set; } = null!;

    public BehaviorContainerNode? Parent { get; private set; }

    public BehaviorBlackboard Blackboard => Tree.Blackboard;

    internal virtual void AttachTo(BehaviorTreeInstance tree, BehaviorContainerNode? parent)
    {
        Tree = tree;
        Parent = parent;
    }

    public void Start()
    {
        if (State != BehaviorNodeState.Inactive)
        {
            throw new InvalidOperationException($"Behavior node '{Name}' can only start from inactive state.");
        }

        State = BehaviorNodeState.Active;
        Tree.EnqueueExecution(OnStart);
    }

    public void Cancel()
    {
        if (State != BehaviorNodeState.Active)
        {
            return;
        }

        State = BehaviorNodeState.StopRequested;
        Tree.EnqueueExecution(OnCancel);
    }

    internal void ParentCompositeStopped(BehaviorCompositeNode composite)
    {
        OnParentCompositeStopped(composite);
    }

    protected void Stopped(bool success)
    {
        if (State == BehaviorNodeState.Inactive)
        {
            return;
        }

        State = BehaviorNodeState.Inactive;
        Tree.OnNodeStopped(this, success);
    }

    protected virtual void OnStart()
    {
    }

    protected virtual void OnCancel()
    {
        Stopped(false);
    }

    protected virtual void OnParentCompositeStopped(BehaviorCompositeNode composite)
    {
    }
}

public abstract class BehaviorContainerNode : BehaviorNode
{
    protected BehaviorContainerNode(string name)
        : base(name)
    {
    }

    internal void ChildStopped(BehaviorNode child, bool succeeded)
    {
        if (State == BehaviorNodeState.Inactive)
        {
            return;
        }

        OnChildStopped(child, succeeded);
    }

    protected abstract void OnChildStopped(BehaviorNode child, bool succeeded);
}

public abstract class BehaviorCompositeNode : BehaviorContainerNode
{
    private readonly List<BehaviorNode> _children;

    protected BehaviorCompositeNode(string name, IEnumerable<BehaviorNode> children)
        : base(name)
    {
        _children = children?.ToList() ?? throw new ArgumentNullException(nameof(children));
        if (_children.Count == 0)
        {
            throw new ArgumentException("Composite behavior nodes require at least one child.", nameof(children));
        }
    }

    public IReadOnlyList<BehaviorNode> Children => _children;

    internal override void AttachTo(BehaviorTreeInstance tree, BehaviorContainerNode? parent)
    {
        base.AttachTo(tree, parent);
        foreach (var child in _children)
        {
            child.AttachTo(tree, this);
        }
    }

    public abstract void StopLowerPriorityChildrenForChild(BehaviorNode child, bool immediateRestart);
}

public class BehaviorDecoratorNode : BehaviorContainerNode
{
    public BehaviorDecoratorNode(string name, BehaviorNode child)
        : base(name)
    {
        Child = child ?? throw new ArgumentNullException(nameof(child));
    }

    public BehaviorNode Child { get; }

    internal override void AttachTo(BehaviorTreeInstance tree, BehaviorContainerNode? parent)
    {
        base.AttachTo(tree, parent);
        Child.AttachTo(tree, this);
    }

    protected override void OnStart()
    {
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
            Stopped(false);
        }
    }

    protected override void OnChildStopped(BehaviorNode child, bool succeeded)
    {
        Stopped(succeeded);
    }

    protected override void OnParentCompositeStopped(BehaviorCompositeNode composite)
    {
        Child.ParentCompositeStopped(composite);
    }
}
