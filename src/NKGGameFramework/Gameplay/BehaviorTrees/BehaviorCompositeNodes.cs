namespace NKGGameFramework.Gameplay;

public sealed class BehaviorSequenceNode : BehaviorCompositeNode
{
    private int _currentIndex;
    private int? _restartIndex;

    public BehaviorSequenceNode(params BehaviorNode[] children)
        : base("Sequence", children)
    {
    }

    public BehaviorSequenceNode(IEnumerable<BehaviorNode> children)
        : base("Sequence", children)
    {
    }

    protected override void OnStart()
    {
        _currentIndex = -1;
        _restartIndex = null;
        ProcessNextChild();
    }

    protected override void OnCancel()
    {
        if (_currentIndex >= 0 && _currentIndex < Children.Count && Children[_currentIndex].IsActive)
        {
            Children[_currentIndex].Cancel();
        }
        else
        {
            Stopped(false);
        }
    }

    protected override void OnChildStopped(BehaviorNode child, bool succeeded)
    {
        if (_restartIndex is { } restartIndex)
        {
            _currentIndex = restartIndex - 1;
            _restartIndex = null;
            ProcessNextChild();
            return;
        }

        if (!succeeded)
        {
            Stopped(false);
            return;
        }

        ProcessNextChild();
    }

    public override void StopLowerPriorityChildrenForChild(BehaviorNode child, bool immediateRestart)
    {
        var childIndex = IndexOfChild(child);
        if (childIndex < 0)
        {
            return;
        }

        for (var i = childIndex + 1; i < Children.Count; i++)
        {
            if (!Children[i].IsActive)
            {
                continue;
            }

            if (immediateRestart)
            {
                _restartIndex = childIndex;
            }
            else
            {
                _currentIndex = Children.Count;
            }

            Children[i].ParentCompositeStopped(this);
            Children[i].Cancel();
            return;
        }
    }

    private void ProcessNextChild()
    {
        if (++_currentIndex >= Children.Count)
        {
            Stopped(true);
            return;
        }

        if (IsStopRequested)
        {
            Stopped(false);
            return;
        }

        Children[_currentIndex].Start();
    }

    private int IndexOfChild(BehaviorNode child)
    {
        for (var i = 0; i < Children.Count; i++)
        {
            if (ReferenceEquals(Children[i], child))
            {
                return i;
            }
        }

        return -1;
    }
}

public sealed class BehaviorSelectorNode : BehaviorCompositeNode
{
    private int _currentIndex;
    private int? _restartIndex;

    public BehaviorSelectorNode(params BehaviorNode[] children)
        : base("Selector", children)
    {
    }

    public BehaviorSelectorNode(IEnumerable<BehaviorNode> children)
        : base("Selector", children)
    {
    }

    protected override void OnStart()
    {
        _currentIndex = -1;
        _restartIndex = null;
        ProcessNextChild();
    }

    protected override void OnCancel()
    {
        if (_currentIndex >= 0 && _currentIndex < Children.Count && Children[_currentIndex].IsActive)
        {
            Children[_currentIndex].Cancel();
        }
        else
        {
            Stopped(false);
        }
    }

    protected override void OnChildStopped(BehaviorNode child, bool succeeded)
    {
        if (_restartIndex is { } restartIndex)
        {
            _currentIndex = restartIndex - 1;
            _restartIndex = null;
            ProcessNextChild();
            return;
        }

        if (succeeded)
        {
            Stopped(true);
            return;
        }

        ProcessNextChild();
    }

    public override void StopLowerPriorityChildrenForChild(BehaviorNode child, bool immediateRestart)
    {
        var childIndex = IndexOfChild(child);
        if (childIndex < 0)
        {
            return;
        }

        for (var i = childIndex + 1; i < Children.Count; i++)
        {
            if (!Children[i].IsActive)
            {
                continue;
            }

            if (immediateRestart)
            {
                _restartIndex = childIndex;
            }
            else
            {
                _currentIndex = Children.Count;
            }

            Children[i].ParentCompositeStopped(this);
            Children[i].Cancel();
            return;
        }
    }

    private void ProcessNextChild()
    {
        if (++_currentIndex >= Children.Count)
        {
            Stopped(false);
            return;
        }

        if (IsStopRequested)
        {
            Stopped(false);
            return;
        }

        Children[_currentIndex].Start();
    }

    private int IndexOfChild(BehaviorNode child)
    {
        for (var i = 0; i < Children.Count; i++)
        {
            if (ReferenceEquals(Children[i], child))
            {
                return i;
            }
        }

        return -1;
    }
}

public sealed class BehaviorParallelNode : BehaviorCompositeNode
{
    private int _remaining;
    private bool _failed;

    public BehaviorParallelNode(params BehaviorNode[] children)
        : base("Parallel", children)
    {
    }

    public BehaviorParallelNode(IEnumerable<BehaviorNode> children)
        : base("Parallel", children)
    {
    }

    protected override void OnStart()
    {
        _failed = false;
        _remaining = Children.Count;
        foreach (var child in Children)
        {
            child.Start();
        }
    }

    protected override void OnCancel()
    {
        var canceled = false;
        foreach (var child in Children)
        {
            if (child.IsActive)
            {
                canceled = true;
                child.Cancel();
            }
        }

        if (!canceled)
        {
            Stopped(false);
        }
    }

    protected override void OnChildStopped(BehaviorNode child, bool succeeded)
    {
        if (!succeeded && !_failed)
        {
            _failed = true;
            foreach (var candidate in Children)
            {
                if (!ReferenceEquals(candidate, child) && candidate.IsActive)
                {
                    candidate.Cancel();
                }
            }
        }

        _remaining--;
        if (_remaining <= 0)
        {
            Stopped(!_failed);
        }
    }

    public override void StopLowerPriorityChildrenForChild(BehaviorNode child, bool immediateRestart)
    {
        if (!immediateRestart)
        {
            return;
        }

        foreach (var candidate in Children)
        {
            if (!ReferenceEquals(candidate, child) && candidate.IsActive)
            {
                candidate.ParentCompositeStopped(this);
                candidate.Cancel();
            }
        }
    }
}
