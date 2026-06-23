namespace NKGGameFramework.Gameplay;

public readonly record struct BehaviorBlackboardChange(
    BehaviorBlackboard Blackboard,
    string Key,
    BehaviorBlackboardChangeKind Kind,
    object? Value);

public sealed class BehaviorBlackboard
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Action<BehaviorBlackboardChange>>> _observers = new(StringComparer.Ordinal);

    public BehaviorBlackboard(BehaviorBlackboard? parent = null)
    {
        Parent = parent;
    }

    public BehaviorBlackboard? Parent { get; }

    public IReadOnlyDictionary<string, object?> Values => _values;

    public bool IsSet(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _values.ContainsKey(key) || Parent?.IsSet(key) == true;
    }

    public bool TryGet(string key, out object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_values.TryGetValue(key, out value))
        {
            return true;
        }

        if (Parent is not null)
        {
            return Parent.TryGet(key, out value);
        }

        value = null;
        return false;
    }

    public bool TryGet<TValue>(string key, out TValue? value)
    {
        if (TryGet(key, out var raw) && raw is TValue typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public TValue? Get<TValue>(string key, TValue? fallback = default)
    {
        return TryGet<TValue>(key, out var value) ? value : fallback;
    }

    public void Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_values.TryGetValue(key, out var current))
        {
            _values[key] = value;
            Notify(new BehaviorBlackboardChange(this, key, BehaviorBlackboardChangeKind.Added, value));
            return;
        }

        if (Equals(current, value))
        {
            return;
        }

        _values[key] = value;
        Notify(new BehaviorBlackboardChange(this, key, BehaviorBlackboardChangeKind.Changed, value));
    }

    public bool Unset(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_values.Remove(key))
        {
            return false;
        }

        Notify(new BehaviorBlackboardChange(this, key, BehaviorBlackboardChangeKind.Removed, null));
        return true;
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
}
