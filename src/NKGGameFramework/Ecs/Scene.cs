using NKGGameFramework.Core;

namespace NKGGameFramework.Ecs;

public sealed class Scene : IDisposable
{
    private readonly MemoryPool<EcsCommandBuffer> _commandBuffers;
    private readonly Dictionary<Type, IComponentStore> _componentStores = [];
    private readonly Dictionary<Type, ISceneComponent> _sceneComponents = [];
    private readonly Stack<int> _freeEntityIds = [];
    private int[] _versions = new int[128];
    private bool[] _alive = new bool[128];
    private int _nextEntityId;
    private int _queryDepth;

    public Scene(string name, IEventBus? events = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        _commandBuffers = new MemoryPool<EcsCommandBuffer>(static () => new EcsCommandBuffer());
        Events = events ?? new EventBus();
        Systems = new SystemGroup(name, this);
    }

    public string Name { get; }

    public IEventBus Events { get; }

    public SystemGroup Systems { get; }

    public GameFrameTime Time { get; private set; } = GameFrameTime.Zero;

    public int EntityCount { get; private set; }

    public IEnumerable<Entity> Entities
    {
        get
        {
            for (var id = 1; id < _alive.Length; id++)
            {
                if (_alive[id])
                {
                    yield return new Entity(this, id, _versions[id]);
                }
            }
        }
    }

    public IReadOnlyList<EcsComponentStoreDebugView> ComponentStores
    {
        get
        {
            var stores = new List<EcsComponentStoreDebugView>(_componentStores.Count);
            foreach (var store in _componentStores.Values)
            {
                stores.Add(new EcsComponentStoreDebugView(
                    store.ComponentType,
                    store.Count,
                    store.EntityIds.ToArray()));
            }

            return stores;
        }
    }

    public IReadOnlyList<EcsComponentStoreDumpBlock> ComponentStoreDumpBlocks
    {
        get
        {
            var blocks = new List<EcsComponentStoreDumpBlock>(_componentStores.Count);
            foreach (var store in _componentStores.Values)
            {
                blocks.Add(store.CreateDumpBlock());
            }

            return blocks;
        }
    }

    internal bool IsQueryActive => _queryDepth > 0;

    public Entity CreateEntity()
    {
        ThrowIfStructuralChangeDuringQuery();

        var id = _freeEntityIds.Count > 0 ? _freeEntityIds.Pop() : ++_nextEntityId;
        EnsureEntityCapacity(id);

        if (_versions[id] == 0)
        {
            _versions[id] = 1;
        }

        _alive[id] = true;
        EntityCount++;

        var entity = new Entity(this, id, _versions[id]);
        Events.Publish(new EntityCreated(entity.ToRef()));
        return entity;
    }

    public void Destroy(Entity entity)
    {
        ThrowIfStructuralChangeDuringQuery();
        EnsureEntity(entity);

        var entityRef = entity.ToRef();
        foreach (var store in _componentStores.Values.ToArray())
        {
            store.RemoveAndNotify(this, entity);
        }

        _alive[entity.Id.Value] = false;
        _versions[entity.Id.Value]++;
        _freeEntityIds.Push(entity.Id.Value);
        EntityCount--;
        Events.Publish(new EntityDestroyed(entityRef));
    }

    public EntityQuery<TComponent> Query<TComponent>()
        where TComponent : struct, IComponent
    {
        return new EntityQuery<TComponent>(this);
    }

    public EntityQuery<TFirst, TSecond> Query<TFirst, TSecond>()
        where TFirst : struct, IComponent
        where TSecond : struct, IComponent
    {
        return new EntityQuery<TFirst, TSecond>(this);
    }

    public EcsCommandBuffer CreateCommandBuffer()
    {
        var commandBuffer = _commandBuffers.Acquire();
        commandBuffer.Initialize(this, _commandBuffers);
        return commandBuffer;
    }

    public TComponent GetOrCreateSceneComponent<TComponent>()
        where TComponent : class, ISceneComponent, new()
    {
        return GetOrCreateSceneComponent(static () => new TComponent());
    }

    public TComponent GetOrCreateSceneComponent<TComponent>(Func<TComponent> factory)
        where TComponent : class, ISceneComponent
    {
        ArgumentNullException.ThrowIfNull(factory);

        var type = typeof(TComponent);
        if (_sceneComponents.TryGetValue(type, out var component))
        {
            return (TComponent)component;
        }

        var created = factory() ?? throw new InvalidOperationException($"Scene component factory returned null for '{type.Name}'.");
        _sceneComponents.Add(type, created);
        return created;
    }

    public bool TryGetSceneComponent<TComponent>(out TComponent component)
        where TComponent : class, ISceneComponent
    {
        if (_sceneComponents.TryGetValue(typeof(TComponent), out var found))
        {
            component = (TComponent)found;
            return true;
        }

        component = null!;
        return false;
    }

    public bool RemoveSceneComponent<TComponent>()
        where TComponent : class, ISceneComponent
    {
        if (!_sceneComponents.Remove(typeof(TComponent), out var component))
        {
            return false;
        }

        if (component is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return true;
    }

    public IReadOnlyList<EcsComponentDebugView> GetComponents(Entity entity)
    {
        EnsureEntity(entity);

        var components = new List<EcsComponentDebugView>();
        foreach (var store in _componentStores.Values)
        {
            if (store.Has(entity.Id.Value))
            {
                components.Add(new EcsComponentDebugView(
                    store.ComponentType,
                    store.GetBoxed(entity.Id.Value)));
            }
        }

        return components;
    }

    public IReadOnlyList<Type> GetComponentTypes(Entity entity)
    {
        EnsureEntity(entity);

        var componentTypes = new List<Type>();
        foreach (var store in _componentStores.Values)
        {
            if (store.Has(entity.Id.Value))
            {
                componentTypes.Add(store.ComponentType);
            }
        }

        return componentTypes;
    }

    public bool TryGetComponent(Entity entity, Type componentType, out object component)
    {
        EnsureEntity(entity);
        ArgumentNullException.ThrowIfNull(componentType);

        if (_componentStores.TryGetValue(componentType, out var store) &&
            store.Has(entity.Id.Value))
        {
            component = store.GetBoxed(entity.Id.Value);
            return true;
        }

        component = null!;
        return false;
    }

    public void SetComponent(Entity entity, Type componentType, object component)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        ArgumentNullException.ThrowIfNull(component);

        if (!typeof(IComponent).IsAssignableFrom(componentType) || !componentType.IsValueType)
        {
            throw new ArgumentException($"Type '{componentType.FullName}' is not a value-type ECS component.", nameof(componentType));
        }

        if (!componentType.IsInstanceOfType(component))
        {
            throw new ArgumentException($"Component value type '{component.GetType().FullName}' does not match '{componentType.FullName}'.", nameof(component));
        }

        var method = typeof(Scene)
            .GetMethod(nameof(SetComponentByRuntimeType), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(componentType);
        method.Invoke(this, [entity, component]);
    }

    public void Update(in GameFrameTime time)
    {
        Time = time;
        Systems.Update(this, in time);
        Events.DispatchQueuedEvents();
    }

    public void Update(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.Advance(Time, deltaTime, realDeltaTime);
        Update(in time);
    }

    public void Dispose()
    {
        Systems.Dispose();
        Events.Clear();
        _commandBuffers.Clear();
        ClearSceneComponents();
        _componentStores.Clear();
        _freeEntityIds.Clear();
        _versions = [];
        _alive = [];
        EntityCount = 0;
    }

    internal bool IsAlive(int id, int version)
    {
        return id > 0 && id < _alive.Length && _alive[id] && _versions[id] == version;
    }

    public bool TryGetEntity(int id, int version, out Entity entity)
    {
        if (IsAlive(id, version))
        {
            entity = new Entity(this, id, version);
            return true;
        }

        entity = default;
        return false;
    }

    public bool TryGetEntity(int id, out Entity entity)
    {
        if (id > 0 && id < _alive.Length && _alive[id])
        {
            entity = new Entity(this, id, _versions[id]);
            return true;
        }

        entity = default;
        return false;
    }

    internal bool HasComponent<TComponent>(Entity entity)
        where TComponent : struct, IComponent
    {
        EnsureEntity(entity);
        return TryGetStore<TComponent>(out var store) && store.Has(entity.Id.Value);
    }

    internal ref TComponent GetComponent<TComponent>(Entity entity)
        where TComponent : struct, IComponent
    {
        EnsureEntity(entity);

        if (!TryGetStore<TComponent>(out var store))
        {
            throw new KeyNotFoundException($"Entity {entity.Id.Value} does not contain component '{typeof(TComponent).Name}'.");
        }

        return ref store.Get(entity.Id.Value);
    }

    internal void SetComponent<TComponent>(Entity entity, TComponent component)
        where TComponent : struct, IComponent
    {
        ThrowIfStructuralChangeDuringQuery();
        EnsureEntity(entity);

        var store = GetOrCreateStore<TComponent>();
        var existed = store.Has(entity.Id.Value);
        store.Set(entity.Id.Value, component);
        ref var current = ref store.Get(entity.Id.Value);

        if (existed)
        {
            Systems.NotifyComponentUpdated(entity, ref current);
            Events.Publish(new ComponentUpdated<TComponent>(entity.ToRef()));
        }
        else
        {
            Systems.NotifyComponentAdded(entity, ref current);
            Events.Publish(new ComponentAdded<TComponent>(entity.ToRef()));
        }
    }

    internal bool RemoveComponent<TComponent>(Entity entity)
        where TComponent : struct, IComponent
    {
        ThrowIfStructuralChangeDuringQuery();
        EnsureEntity(entity);

        if (!TryGetStore<TComponent>(out var store) || !store.RemoveAndNotify(this, entity))
        {
            return false;
        }

        return true;
    }

    internal void NotifyComponentRemoved<TComponent>(Entity entity, in TComponent component)
        where TComponent : struct, IComponent
    {
        Systems.NotifyComponentRemoved(entity, in component);
        Events.Publish(new ComponentRemoved<TComponent>(entity.ToRef()));
    }

    internal bool TryGetStore<TComponent>(out ComponentStore<TComponent> store)
        where TComponent : struct, IComponent
    {
        if (_componentStores.TryGetValue(typeof(TComponent), out var found))
        {
            store = (ComponentStore<TComponent>)found;
            return true;
        }

        store = null!;
        return false;
    }

    internal void EnterQuery()
    {
        _queryDepth++;
    }

    internal void ExitQuery()
    {
        _queryDepth--;
    }

    private ComponentStore<TComponent> GetOrCreateStore<TComponent>()
        where TComponent : struct, IComponent
    {
        if (TryGetStore<TComponent>(out var store))
        {
            return store;
        }

        store = new ComponentStore<TComponent>();
        _componentStores.Add(typeof(TComponent), store);
        return store;
    }

    private void SetComponentByRuntimeType<TComponent>(Entity entity, object component)
        where TComponent : struct, IComponent
    {
        SetComponent(entity, (TComponent)component);
    }

    private void EnsureEntity(Entity entity)
    {
        if (!IsAlive(entity.Id.Value, entity.Version))
        {
            throw new ObjectDisposedException(nameof(Entity), $"Entity {entity.Id.Value} is not alive in scene '{Name}'.");
        }
    }

    private void EnsureEntityCapacity(int id)
    {
        if (id < _versions.Length)
        {
            return;
        }

        var newSize = _versions.Length;
        while (newSize <= id)
        {
            newSize *= 2;
        }

        Array.Resize(ref _versions, newSize);
        Array.Resize(ref _alive, newSize);
    }

    private void ThrowIfStructuralChangeDuringQuery()
    {
        if (IsQueryActive)
        {
            throw new InvalidOperationException("Structural ECS changes are not allowed during query iteration. Record them in an EcsCommandBuffer and play it back after the query.");
        }
    }

    private void ClearSceneComponents()
    {
        foreach (var component in _sceneComponents.Values)
        {
            if (component is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _sceneComponents.Clear();
    }
}
