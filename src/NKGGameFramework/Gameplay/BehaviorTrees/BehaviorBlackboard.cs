using NKGGameFramework.Ecs;
using OdinSerializer;

namespace NKGGameFramework.Gameplay;

public readonly record struct BehaviorBlackboardChange
{
    public BehaviorBlackboardChange(
        BehaviorBlackboard blackboard,
        string key,
        BehaviorBlackboardChangeKind kind,
        BehaviorBlackboardValue? value)
    {
        Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Kind = kind;
        Value = value;
    }

    public BehaviorBlackboard Blackboard { get; }

    public string Key { get; }

    public BehaviorBlackboardChangeKind Kind { get; }

    public BehaviorBlackboardValue? Value { get; }

    public bool TryGet<TValue>(out TValue? value)
    {
        if (Value is not null && Value.TryGet(out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

public sealed class BehaviorBlackboard : IDisposable
{
    [OdinSerialize]
    private readonly Dictionary<string, BehaviorBlackboardValue> _values = new(StringComparer.Ordinal);

    [NonSerialized]
    private readonly Dictionary<string, List<Action<BehaviorBlackboardChange>>> _observers = new(StringComparer.Ordinal);

    [NonSerialized]
    private readonly BehaviorBlackboardValuePool _valuePool;

    public BehaviorBlackboard(Scene scene, BehaviorBlackboard? parent = null)
        : this(GetSceneValuePool(scene), parent)
    {
    }

    internal BehaviorBlackboard(BehaviorBlackboardValuePool valuePool, BehaviorBlackboard? parent = null)
    {
        ArgumentNullException.ThrowIfNull(valuePool);
        Parent = parent;
        _valuePool = parent?._valuePool ?? valuePool;
    }

    [field: NonSerialized]
    public BehaviorBlackboard? Parent { get; }

    public IReadOnlyDictionary<string, BehaviorBlackboardValue> Values => _values;

    public static BehaviorBlackboard CreateForScene(Scene scene, BehaviorBlackboard? parent = null)
    {
        return new BehaviorBlackboard(scene, parent);
    }

    public bool IsSet(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _values.ContainsKey(key) || Parent?.IsSet(key) == true;
    }

    public bool TryGetValue(string key, out BehaviorBlackboardValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_values.TryGetValue(key, out value!))
        {
            return true;
        }

        if (Parent is not null)
        {
            return Parent.TryGetValue(key, out value);
        }

        value = null!;
        return false;
    }

    public bool TryGet<TValue>(string key, out TValue? value)
    {
        if (TryGetValue(key, out var raw) && raw.TryGet(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public TValue? Get<TValue>(string key, TValue? fallback = default)
    {
        return TryGet<TValue>(key, out var value) ? value : fallback;
    }

    public void Set<TValue>(string key, TValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        SetTypedValue(key, value);
    }

    public bool Unset(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_values.Remove(key, out var current))
        {
            return false;
        }

        Notify(new BehaviorBlackboardChange(this, key, BehaviorBlackboardChangeKind.Removed, null));
        _valuePool.Release(current);
        return true;
    }

    public void Clear()
    {
        foreach (var value in _values.Values)
        {
            _valuePool.Release(value);
        }

        _values.Clear();
    }

    public void Dispose()
    {
        Clear();
        _observers.Clear();
    }

    public void AddObserver(string key, Action<BehaviorBlackboardChange> observer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(observer);

        if (!_observers.TryGetValue(key, out var observers))
        {
            observers = [];
            _observers.Add(key, observers);
        }

        if (!observers.Contains(observer))
        {
            observers.Add(observer);
        }
    }

    public void RemoveObserver(string key, Action<BehaviorBlackboardChange> observer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(observer);

        if (_observers.TryGetValue(key, out var observers))
        {
            observers.Remove(observer);
            if (observers.Count == 0)
            {
                _observers.Remove(key);
            }
        }
    }

    private void SetTypedValue<TValue>(string key, TValue? value)
    {
        if (!_values.TryGetValue(key, out var current))
        {
            var next = _valuePool.Rent(value);
            _values[key] = next;
            Notify(new BehaviorBlackboardChange(this, key, BehaviorBlackboardChangeKind.Added, next));
            return;
        }

        if (current is BehaviorBlackboardValue<TValue> typed)
        {
            if (EqualityComparer<TValue>.Default.Equals(typed.Value, value))
            {
                return;
            }

            typed.SetValue(value);
            Notify(new BehaviorBlackboardChange(this, key, BehaviorBlackboardChangeKind.Changed, current));
            return;
        }

        var replacement = _valuePool.Rent(value);
        if (current.ValueEquals(replacement))
        {
            _valuePool.Release(replacement);
            return;
        }

        _values[key] = replacement;
        Notify(new BehaviorBlackboardChange(this, key, BehaviorBlackboardChangeKind.Changed, replacement));
        _valuePool.Release(current);
    }

    private void Notify(BehaviorBlackboardChange change)
    {
        if (!_observers.TryGetValue(change.Key, out var observers) || observers.Count == 0)
        {
            return;
        }

        foreach (var observer in observers.ToArray())
        {
            observer(change);
        }
    }

    private static BehaviorBlackboardValuePool GetSceneValuePool(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.GetOrCreateSceneComponent<BehaviorBlackboardPoolComponent>().ValuePool;
    }
}
