namespace NKGGameFramework.Ecs;

internal interface IComponentStore
{
    Type ComponentType { get; }

    int Count { get; }

    long Version { get; }

    IReadOnlyList<int> EntityIds { get; }

    EcsComponentStoreDumpBlock CreateDumpBlock();

    bool Has(int entityId);

    bool Remove(int entityId);

    bool RemoveAndNotify(Scene scene, Entity entity);

    object GetBoxed(int entityId);
}

internal sealed class ComponentStore<TComponent> : IComponentStore
    where TComponent : struct, IComponent
{
    private TComponent[] _components = new TComponent[128];
    private bool[] _has = new bool[128];
    private readonly List<int> _entityIds = [];
    private readonly Dictionary<int, int> _positions = [];

    public Type ComponentType => typeof(TComponent);

    public int Count => _entityIds.Count;

    public long Version { get; private set; }

    public IReadOnlyList<int> EntityIds => _entityIds;

    public EcsComponentStoreDumpBlock CreateDumpBlock()
    {
        var entityIds = _entityIds.ToArray();
        var values = new TComponent[entityIds.Length];
        for (var index = 0; index < entityIds.Length; index++)
        {
            values[index] = _components[entityIds[index]];
        }

        return new EcsComponentStoreDumpBlock(
            typeof(TComponent),
            entityIds,
            values,
            Version);
    }

    public bool Has(int entityId)
    {
        return entityId >= 0 && entityId < _has.Length && _has[entityId];
    }

    public ref TComponent Get(int entityId)
    {
        if (!Has(entityId))
        {
            throw new KeyNotFoundException($"Entity {entityId} does not contain component '{typeof(TComponent).Name}'.");
        }

        return ref _components[entityId];
    }

    public object GetBoxed(int entityId)
    {
        return Get(entityId);
    }

    public void Set(int entityId, in TComponent component)
    {
        EnsureCapacity(entityId);

        if (!_has[entityId])
        {
            _positions.Add(entityId, _entityIds.Count);
            _entityIds.Add(entityId);
            _has[entityId] = true;
        }

        _components[entityId] = component;
        Version++;
    }

    public bool Remove(int entityId)
    {
        if (!Has(entityId))
        {
            return false;
        }

        _has[entityId] = false;
        _components[entityId] = default;

        var index = _positions[entityId];
        var lastIndex = _entityIds.Count - 1;
        var lastEntityId = _entityIds[lastIndex];
        _entityIds[index] = lastEntityId;
        _positions[lastEntityId] = index;
        _entityIds.RemoveAt(lastIndex);
        _positions.Remove(entityId);
        Version++;
        return true;
    }

    public bool RemoveAndNotify(Scene scene, Entity entity)
    {
        if (!Has(entity.Id.Value))
        {
            return false;
        }

        var component = _components[entity.Id.Value];
        var removed = Remove(entity.Id.Value);

        if (removed)
        {
            scene.NotifyComponentRemoved(entity, in component);
        }

        return removed;
    }

    private void EnsureCapacity(int entityId)
    {
        if (entityId < _components.Length)
        {
            return;
        }

        var newSize = _components.Length;
        while (newSize <= entityId)
        {
            newSize *= 2;
        }

        Array.Resize(ref _components, newSize);
        Array.Resize(ref _has, newSize);
    }
}
