namespace NKGGameFramework.Core;

public enum EventExceptionPolicy
{
    Throw,
    Continue,
}

public interface IEventBus
{
    int QueuedEventCount { get; }

    IDisposable Subscribe<TEvent>(Action<TEvent> handler);

    bool Unsubscribe<TEvent>(Action<TEvent> handler);

    bool HasHandler<TEvent>(Action<TEvent>? handler = null);

    void Publish<TEvent>(TEvent evt);

    void FireNow<TEvent>(TEvent evt);

    void Fire<TEvent>(TEvent evt);

    void FirePooled<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new();

    void FireNowPooled<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new();

    TEvent Rent<TEvent>()
        where TEvent : GameEventArgs, new();

    void Return<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new();

    int DispatchQueuedEvents(int maxEvents = int.MaxValue);

    void Clear();
}

public abstract class GameEventArgs : IPoolItem
{
    public int EventId => GameEventTypeId.Get(GetType());

    public virtual void OnAcquire()
    {
    }

    public virtual void OnRelease()
    {
        Clear();
    }

    public virtual void Clear()
    {
    }
}

public sealed class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly Queue<IQueuedEvent> _queuedEvents = [];
    private readonly Dictionary<Type, IGameEventArgsPool> _eventArgsPools = [];

    public int QueuedEventCount => _queuedEvents.Count;

    public EventExceptionPolicy ExceptionPolicy { get; set; } = EventExceptionPolicy.Throw;

    public Action<Exception, Type>? ExceptionHandler { get; set; }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            list = [];
            _handlers.Add(typeof(TEvent), list);
        }

        if (list.Contains(handler))
        {
            throw new InvalidOperationException($"Handler is already subscribed to event '{typeof(TEvent).Name}'.");
        }

        list.Add(handler);

        return new Subscription(() => Unsubscribe(handler));
    }

    public bool Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            return false;
        }

        var removed = list.Remove(handler);
        if (list.Count == 0)
        {
            _handlers.Remove(typeof(TEvent));
        }

        return removed;
    }

    public bool HasHandler<TEvent>(Action<TEvent>? handler = null)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            return false;
        }

        return handler is null ? list.Count > 0 : list.Contains(handler);
    }

    public void Publish<TEvent>(TEvent evt)
    {
        FireNow(evt);
    }

    public void FireNow<TEvent>(TEvent evt)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            return;
        }

        foreach (var handler in list.ToArray())
        {
            try
            {
                ((Action<TEvent>)handler).Invoke(evt);
            }
            catch (Exception ex) when (ExceptionPolicy == EventExceptionPolicy.Continue)
            {
                ExceptionHandler?.Invoke(ex, typeof(TEvent));
            }
        }
    }

    public void Fire<TEvent>(TEvent evt)
    {
        _queuedEvents.Enqueue(new QueuedEvent<TEvent>(evt, releaseAfterDispatch: false));
    }

    public void FirePooled<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new()
    {
        GetOrCreatePool(typeof(TEvent));
        _queuedEvents.Enqueue(new QueuedEvent<TEvent>(evt, releaseAfterDispatch: true));
    }

    public void FireNowPooled<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new()
    {
        try
        {
            FireNow(evt);
        }
        finally
        {
            Return(evt);
        }
    }

    public TEvent Rent<TEvent>()
        where TEvent : GameEventArgs, new()
    {
        return ((GameEventArgsPool<TEvent>)GetOrCreatePool(typeof(TEvent))).Rent();
    }

    public void Return<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new()
    {
        ArgumentNullException.ThrowIfNull(evt);
        ((GameEventArgsPool<TEvent>)GetOrCreatePool(typeof(TEvent))).Return(evt);
    }

    public int DispatchQueuedEvents(int maxEvents = int.MaxValue)
    {
        if (maxEvents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEvents));
        }

        var dispatched = 0;
        while (_queuedEvents.Count > 0 && dispatched < maxEvents)
        {
            _queuedEvents.Dequeue().Dispatch(this);
            dispatched++;
        }

        return dispatched;
    }

    public void Clear()
    {
        _handlers.Clear();
        _queuedEvents.Clear();

        foreach (var pool in _eventArgsPools.Values)
        {
            pool.Clear();
        }

        _eventArgsPools.Clear();
    }

    private void Return(GameEventArgs evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (!_eventArgsPools.TryGetValue(evt.GetType(), out var pool))
        {
            throw new InvalidOperationException($"Event args '{evt.GetType().Name}' was not rented by this event bus.");
        }

        pool.Return(evt);
    }

    private IGameEventArgsPool GetOrCreatePool(Type eventType)
    {
        if (_eventArgsPools.TryGetValue(eventType, out var pool))
        {
            return pool;
        }

        pool = (IGameEventArgsPool)Activator.CreateInstance(typeof(GameEventArgsPool<>).MakeGenericType(eventType))!;
        _eventArgsPools.Add(eventType, pool);
        return pool;
    }

    private interface IQueuedEvent
    {
        void Dispatch(EventBus eventBus);
    }

    private sealed class QueuedEvent<TEvent>(TEvent evt, bool releaseAfterDispatch) : IQueuedEvent
    {
        public void Dispatch(EventBus eventBus)
        {
            try
            {
                eventBus.FireNow(evt);
            }
            finally
            {
                if (releaseAfterDispatch && evt is GameEventArgs eventArgs)
                {
                    eventBus.Return(eventArgs);
                }
            }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dispose();
        }
    }

    private interface IGameEventArgsPool
    {
        void Return(GameEventArgs evt);

        void Clear();
    }

    private sealed class GameEventArgsPool<TEvent> : IGameEventArgsPool
        where TEvent : GameEventArgs, new()
    {
        private readonly MemoryPool<TEvent> _pool = new();

        public TEvent Rent()
        {
            return _pool.Acquire();
        }

        public void Return(GameEventArgs evt)
        {
            _pool.Release((TEvent)evt);
        }

        public void Clear()
        {
            _pool.Clear();
        }
    }
}

public sealed class EventModule(EventBus? eventBus = null) : Module, IUpdateModule, IEventBus
{
    private readonly EventBus _eventBus = eventBus ?? new EventBus();

    public override int Priority => int.MinValue;

    public int QueuedEventCount => _eventBus.QueuedEventCount;

    public EventExceptionPolicy ExceptionPolicy
    {
        get => _eventBus.ExceptionPolicy;
        set => _eventBus.ExceptionPolicy = value;
    }

    public Action<Exception, Type>? ExceptionHandler
    {
        get => _eventBus.ExceptionHandler;
        set => _eventBus.ExceptionHandler = value;
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        return _eventBus.Subscribe(handler);
    }

    public bool Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        return _eventBus.Unsubscribe(handler);
    }

    public bool HasHandler<TEvent>(Action<TEvent>? handler = null)
    {
        return _eventBus.HasHandler(handler);
    }

    public void Publish<TEvent>(TEvent evt)
    {
        _eventBus.Publish(evt);
    }

    public void FireNow<TEvent>(TEvent evt)
    {
        _eventBus.FireNow(evt);
    }

    public void Fire<TEvent>(TEvent evt)
    {
        _eventBus.Fire(evt);
    }

    public void FirePooled<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new()
    {
        _eventBus.FirePooled(evt);
    }

    public void FireNowPooled<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new()
    {
        _eventBus.FireNowPooled(evt);
    }

    public TEvent Rent<TEvent>()
        where TEvent : GameEventArgs, new()
    {
        return _eventBus.Rent<TEvent>();
    }

    public void Return<TEvent>(TEvent evt)
        where TEvent : GameEventArgs, new()
    {
        _eventBus.Return(evt);
    }

    public int DispatchQueuedEvents(int maxEvents = int.MaxValue)
    {
        return _eventBus.DispatchQueuedEvents(maxEvents);
    }

    public void Clear()
    {
        _eventBus.Clear();
    }

    public void Update(in GameFrameTime time)
    {
        _eventBus.DispatchQueuedEvents();
    }

    protected override void OnShutdown()
    {
        _eventBus.Clear();
    }
}

internal static class GameEventTypeId
{
    private static readonly Dictionary<Type, int> Ids = [];
    private static int nextId;

    public static int Get(Type type)
    {
        if (Ids.TryGetValue(type, out var id))
        {
            return id;
        }

        id = ++nextId;
        Ids.Add(type, id);
        return id;
    }
}
