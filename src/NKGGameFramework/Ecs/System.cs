namespace NKGGameFramework.Ecs;

public interface ISystem
{
    int Order { get; }

    bool Enabled => true;

    void Update(Scene scene, in SystemUpdateContext context);
}

public readonly record struct SystemUpdateContext(double DeltaTime, double RealDeltaTime, EcsCommandBuffer Commands);

public interface ISystemLifecycle
{
    void OnCreate(Scene scene);

    void OnStartRunning(Scene scene);

    void OnStopRunning(Scene scene);

    void OnDestroy(Scene scene);
}

public interface IComponentAddedSystem<TComponent>
    where TComponent : struct, IComponent
{
    void OnComponentAdded(Scene scene, Entity entity, ref TComponent component);
}

public interface IComponentUpdatedSystem<TComponent>
    where TComponent : struct, IComponent
{
    void OnComponentUpdated(Scene scene, Entity entity, ref TComponent component);
}

public interface IComponentRemovedSystem<TComponent>
    where TComponent : struct, IComponent
{
    void OnComponentRemoved(Scene scene, Entity entity, in TComponent component);
}

public abstract class EcsSystem : ISystem, ISystemLifecycle
{
    protected EcsSystem(int order = 0)
    {
        Order = order;
    }

    public int Order { get; }

    public bool Enabled { get; set; } = true;

    public abstract void Update(Scene scene, in SystemUpdateContext context);

    protected virtual void OnCreate(Scene scene)
    {
    }

    protected virtual void OnStartRunning(Scene scene)
    {
    }

    protected virtual void OnStopRunning(Scene scene)
    {
    }

    protected virtual void OnDestroy(Scene scene)
    {
    }

    void ISystemLifecycle.OnCreate(Scene scene) => OnCreate(scene);

    void ISystemLifecycle.OnStartRunning(Scene scene) => OnStartRunning(scene);

    void ISystemLifecycle.OnStopRunning(Scene scene) => OnStopRunning(scene);

    void ISystemLifecycle.OnDestroy(Scene scene) => OnDestroy(scene);
}

public abstract class QuerySystem<TComponent> : EcsSystem
    where TComponent : struct, IComponent
{
    protected QuerySystem(int order = 0)
        : base(order)
    {
    }

    public sealed override void Update(Scene scene, in SystemUpdateContext context)
    {
        OnUpdate(scene.Query<TComponent>(), in context);
    }

    protected abstract void OnUpdate(EntityQuery<TComponent> query, in SystemUpdateContext context);
}

public abstract class QuerySystem<TFirst, TSecond> : EcsSystem
    where TFirst : struct, IComponent
    where TSecond : struct, IComponent
{
    protected QuerySystem(int order = 0)
        : base(order)
    {
    }

    public sealed override void Update(Scene scene, in SystemUpdateContext context)
    {
        OnUpdate(scene.Query<TFirst, TSecond>(), in context);
    }

    protected abstract void OnUpdate(EntityQuery<TFirst, TSecond> query, in SystemUpdateContext context);
}

public sealed class SystemGroup : IDisposable
{
    private readonly List<ISystem> _systems = [];
    private readonly HashSet<ISystem> _runningSystems = [];
    private readonly Scene _scene;
    private bool _dirty;
    private bool _disposed;

    internal SystemGroup(string name, Scene scene)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scene);

        Name = name;
        _scene = scene;
    }

    public string Name { get; }

    public IReadOnlyList<ISystem> Systems => _systems;

    public void Add(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        ThrowIfDisposed();

        _systems.Add(system);
        _dirty = true;
        NotifyCreated(system);
    }

    public void Update(Scene scene, double deltaTime, double realDeltaTime)
    {
        ThrowIfDisposed();
        EnsureScene(scene);
        EnsureSorted();

        foreach (var system in _systems)
        {
            if (!system.Enabled)
            {
                StopIfRunning(system);
                continue;
            }

            StartIfNeeded(system);

            using var commands = scene.CreateCommandBuffer();
            var context = new SystemUpdateContext(deltaTime, realDeltaTime, commands);
            system.Update(scene, in context);
            commands.Playback();
        }
    }

    internal void NotifyComponentAdded<TComponent>(Entity entity, ref TComponent component)
        where TComponent : struct, IComponent
    {
        EnsureSorted();

        foreach (var system in _systems)
        {
            if (system.Enabled && system is IComponentAddedSystem<TComponent> handler)
            {
                handler.OnComponentAdded(_scene, entity, ref component);
            }
        }
    }

    internal void NotifyComponentUpdated<TComponent>(Entity entity, ref TComponent component)
        where TComponent : struct, IComponent
    {
        EnsureSorted();

        foreach (var system in _systems)
        {
            if (system.Enabled && system is IComponentUpdatedSystem<TComponent> handler)
            {
                handler.OnComponentUpdated(_scene, entity, ref component);
            }
        }
    }

    internal void NotifyComponentRemoved<TComponent>(Entity entity, in TComponent component)
        where TComponent : struct, IComponent
    {
        EnsureSorted();

        foreach (var system in _systems)
        {
            if (system.Enabled && system is IComponentRemovedSystem<TComponent> handler)
            {
                handler.OnComponentRemoved(_scene, entity, in component);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        EnsureSorted();

        for (var i = _systems.Count - 1; i >= 0; i--)
        {
            StopIfRunning(_systems[i]);
        }

        for (var i = _systems.Count - 1; i >= 0; i--)
        {
            NotifyDestroyed(_systems[i]);
        }

        _runningSystems.Clear();
        _systems.Clear();
        _disposed = true;
    }

    private void EnsureSorted()
    {
        if (!_dirty)
        {
            return;
        }

        _systems.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        _dirty = false;
    }

    private void StartIfNeeded(ISystem system)
    {
        if (_runningSystems.Add(system) && system is ISystemLifecycle lifecycle)
        {
            lifecycle.OnStartRunning(_scene);
        }
    }

    private void StopIfRunning(ISystem system)
    {
        if (_runningSystems.Remove(system) && system is ISystemLifecycle lifecycle)
        {
            lifecycle.OnStopRunning(_scene);
        }
    }

    private void NotifyCreated(ISystem system)
    {
        if (system is ISystemLifecycle lifecycle)
        {
            lifecycle.OnCreate(_scene);
        }
    }

    private void NotifyDestroyed(ISystem system)
    {
        if (system is ISystemLifecycle lifecycle)
        {
            lifecycle.OnDestroy(_scene);
        }
    }

    private void EnsureScene(Scene scene)
    {
        if (!ReferenceEquals(_scene, scene))
        {
            throw new InvalidOperationException($"System group '{Name}' can only update scene '{_scene.Name}'.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SystemGroup));
        }
    }
}
