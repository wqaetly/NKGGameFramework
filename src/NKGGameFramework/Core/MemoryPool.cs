namespace NKGGameFramework.Core;

public interface IPoolItem
{
    void OnAcquire();

    void OnRelease();
}

public sealed class MemoryPool<T>
    where T : class, IPoolItem
{
    private readonly Func<T> _factory;
    private readonly HashSet<T> _leased = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<T> _available = new();

    public MemoryPool(Func<T>? factory = null)
    {
        _factory = factory ?? CreateDefaultFactory();
    }

    public int AvailableCount => _available.Count;

    public int LeasedCount => _leased.Count;

    public T Acquire()
    {
        var item = _available.Count > 0 ? _available.Pop() : _factory();
        _leased.Add(item);
        item.OnAcquire();
        return item;
    }

    public void Release(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!_leased.Remove(item))
        {
            throw new InvalidOperationException($"The {typeof(T).Name} instance was not leased by this pool or was already released.");
        }

        item.OnRelease();
        _available.Push(item);
    }

    public void Clear()
    {
        _available.Clear();
        _leased.Clear();
    }

    private static Func<T> CreateDefaultFactory()
    {
        if (typeof(T).GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException($"Type '{typeof(T).Name}' must have a public parameterless constructor or an explicit pool factory.");
        }

        return static () => Activator.CreateInstance<T>();
    }
}
