namespace NKGGameFramework.Core;

public abstract class PoolObject
{
    public abstract string Name { get; }

    public abstract object Target { get; }

    public virtual int Priority => 0;

    public DateTimeOffset LastUseTime { get; internal set; }

    public int SpawnCount { get; internal set; }

    protected internal virtual void OnSpawn()
    {
    }

    protected internal virtual void OnUnspawn()
    {
    }

    protected internal virtual void OnRelease()
    {
    }
}

public sealed class ObjectPool<T>
    where T : PoolObject
{
    private readonly Dictionary<object, T> _objects = new(ReferenceEqualityComparer.Instance);

    public ObjectPool(string name, int capacity = int.MaxValue, TimeSpan? expireAfter = null, bool allowMultiSpawn = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Name = name;
        Capacity = capacity;
        ExpireAfter = expireAfter ?? TimeSpan.MaxValue;
        AllowMultiSpawn = allowMultiSpawn;
    }

    public string Name { get; }

    public int Capacity { get; set; }

    public TimeSpan ExpireAfter { get; set; }

    public bool AllowMultiSpawn { get; }

    public int Count => _objects.Count;

    public void Register(T item, bool spawned = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_objects.ContainsKey(item.Target))
        {
            throw new FrameworkException($"Object '{item.Name}' is already registered in pool '{Name}'.");
        }

        item.SpawnCount = spawned ? 1 : 0;
        item.LastUseTime = DateTimeOffset.UtcNow;
        _objects.Add(item.Target, item);
        ReleaseOverflow();
    }

    public T? Spawn(string? name = null)
    {
        foreach (var item in _objects.Values)
        {
            if (!string.IsNullOrEmpty(name) && item.Name != name)
            {
                continue;
            }

            if (!AllowMultiSpawn && item.SpawnCount > 0)
            {
                continue;
            }

            item.SpawnCount++;
            item.LastUseTime = DateTimeOffset.UtcNow;
            item.OnSpawn();
            return item;
        }

        return null;
    }

    public void Unspawn(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_objects.TryGetValue(target, out var item))
        {
            throw new FrameworkException($"Target is not registered in pool '{Name}'.");
        }

        if (item.SpawnCount <= 0)
        {
            throw new FrameworkException($"Object '{item.Name}' is not spawned.");
        }

        item.SpawnCount--;
        item.LastUseTime = DateTimeOffset.UtcNow;
        item.OnUnspawn();
    }

    public int ReleaseExpired(DateTimeOffset now)
    {
        var candidates = _objects.Values
            .Where(static item => item.SpawnCount == 0)
            .Where(item => now - item.LastUseTime >= ExpireAfter)
            .OrderBy(static item => item.Priority)
            .ToArray();

        foreach (var candidate in candidates)
        {
            Release(candidate.Target);
        }

        return candidates.Length;
    }

    public bool Release(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_objects.Remove(target, out var item))
        {
            return false;
        }

        item.OnRelease();
        return true;
    }

    private void ReleaseOverflow()
    {
        while (_objects.Count > Capacity)
        {
            var candidate = _objects.Values
                .Where(static item => item.SpawnCount == 0)
                .OrderBy(static item => item.Priority)
                .ThenBy(static item => item.LastUseTime)
                .FirstOrDefault();

            if (candidate is null)
            {
                break;
            }

            Release(candidate.Target);
        }
    }
}

