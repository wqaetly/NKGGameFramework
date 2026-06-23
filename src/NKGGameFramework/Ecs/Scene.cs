using NKGGameFramework.Core;

namespace NKGGameFramework.Ecs;

public sealed class Scene : IDisposable
{
    private readonly MemoryPool<EcsCommandBuffer> _commandBuffers;
    private readonly Dictionary<Type, IComponentStore> _componentStores = [];
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

    public int EntityCount { get; private set; }

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

    public void Update(double deltaTime, double realDeltaTime)
    {
        Systems.Update(this, deltaTime, realDeltaTime);
        Events.DispatchQueuedEvents();
    }

    public void Dispose()
    {
        Systems.Dispose();
        Events.Clear();
        _commandBuffers.Clear();
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

    internal bool TryGetEntity(int id, int version, out Entity entity)
    {
        if (IsAlive(id, version))
        {
            entity = new Entity(this, id, version);
            return true;
        }

        entity = default;
        return false;
    }

    internal bool TryGetEntity(int id, out Entity entity)
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
}
