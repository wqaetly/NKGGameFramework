namespace NKGGameFramework.Core;

public sealed class Fsm<TOwner>
    where TOwner : class
{
    private readonly Dictionary<Type, FsmState<TOwner>> _states = [];
    private FsmState<TOwner>? _currentState;

    public Fsm(string name, TOwner owner, IEnumerable<FsmState<TOwner>> states)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(states);

        Name = name;
        Owner = owner;

        foreach (var state in states)
        {
            var stateType = state.GetType();
            if (!_states.TryAdd(stateType, state))
            {
                throw new FrameworkException($"FSM '{name}' already contains state '{stateType.Name}'.");
            }

            state.Initialize(this);
        }

        if (_states.Count == 0)
        {
            throw new FrameworkException($"FSM '{name}' must contain at least one state.");
        }
    }

    public string Name { get; }

    public TOwner Owner { get; }

    public FsmState<TOwner>? CurrentState => _currentState;

    public IReadOnlyCollection<FsmState<TOwner>> States => _states.Values;

    public double CurrentStateTime { get; private set; }

    public bool IsRunning => _currentState is not null;

    public bool HasState<TState>()
        where TState : FsmState<TOwner>
    {
        return HasState(typeof(TState));
    }

    public bool HasState(Type stateType)
    {
        ArgumentNullException.ThrowIfNull(stateType);
        return _states.ContainsKey(stateType);
    }

    public TState GetState<TState>()
        where TState : FsmState<TOwner>
    {
        return (TState)GetState(typeof(TState));
    }

    public FsmState<TOwner> GetState(Type stateType)
    {
        ArgumentNullException.ThrowIfNull(stateType);

        if (!_states.TryGetValue(stateType, out var state))
        {
            throw new FrameworkException($"FSM '{Name}' does not contain state '{stateType.Name}'.");
        }

        return state;
    }

    public void Start<TState>()
        where TState : FsmState<TOwner>
    {
        Start(typeof(TState));
    }

    public void Start(Type stateType)
    {
        ArgumentNullException.ThrowIfNull(stateType);

        if (IsRunning)
        {
            throw new FrameworkException($"FSM '{Name}' is already running.");
        }

        ChangeState(stateType);
    }

    public void ChangeState<TState>()
        where TState : FsmState<TOwner>
    {
        ChangeState(typeof(TState));
    }

    public void ChangeState(Type stateType)
    {
        if (!_states.TryGetValue(stateType, out var nextState))
        {
            throw new FrameworkException($"FSM '{Name}' does not contain state '{stateType.Name}'.");
        }

        var previousState = _currentState;
        previousState?.Leave(this, isShutdown: false);

        _currentState = nextState;
        CurrentStateTime = 0;
        nextState.Enter(this);
    }

    public void Update(in GameFrameTime time)
    {
        if (_currentState is null)
        {
            return;
        }

        CurrentStateTime += time.DeltaSeconds;
        _currentState.Update(this, in time);
    }

    public void Update(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.FromSeconds(deltaTime, realDeltaTime);
        Update(in time);
    }

    public void Shutdown()
    {
        _currentState?.Leave(this, isShutdown: true);
        _currentState = null;

        foreach (var state in _states.Values)
        {
            state.Destroy(this);
        }

        _states.Clear();
        CurrentStateTime = 0;
    }
}

public abstract class FsmState<TOwner>
    where TOwner : class
{
    internal void Initialize(Fsm<TOwner> fsm)
    {
        OnInitialize(fsm);
    }

    internal void Enter(Fsm<TOwner> fsm)
    {
        OnEnter(fsm);
    }

    internal void Update(Fsm<TOwner> fsm, in GameFrameTime time)
    {
        OnUpdate(fsm, in time);
    }

    internal void Leave(Fsm<TOwner> fsm, bool isShutdown)
    {
        OnLeave(fsm, isShutdown);
    }

    internal void Destroy(Fsm<TOwner> fsm)
    {
        OnDestroy(fsm);
    }

    protected virtual void OnInitialize(Fsm<TOwner> fsm)
    {
    }

    protected virtual void OnEnter(Fsm<TOwner> fsm)
    {
    }

    protected virtual void OnUpdate(Fsm<TOwner> fsm, in GameFrameTime time)
    {
        OnUpdate(fsm, time.DeltaSeconds, time.RealDeltaSeconds);
    }

    protected virtual void OnUpdate(Fsm<TOwner> fsm, double deltaTime, double realDeltaTime)
    {
    }

    protected virtual void OnLeave(Fsm<TOwner> fsm, bool isShutdown)
    {
    }

    protected virtual void OnDestroy(Fsm<TOwner> fsm)
    {
    }
}
