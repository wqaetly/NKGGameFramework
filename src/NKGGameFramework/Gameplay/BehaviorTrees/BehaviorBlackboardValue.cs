using NKGGameFramework.Core;

namespace NKGGameFramework.Gameplay;

public abstract class BehaviorBlackboardValue : IPoolItem
{
    public abstract Type ValueType { get; }

    public bool TryGet<TValue>(out TValue? value)
    {
        if (this is BehaviorBlackboardValue<TValue> typed)
        {
            value = typed.Value;
            return true;
        }

        value = default;
        return false;
    }

    public virtual void OnAcquire()
    {
    }

    public virtual void OnRelease()
    {
    }

    public static BehaviorBlackboardValue Create<TValue>(TValue? value)
    {
        return new BehaviorBlackboardValue<TValue>(value);
    }

    internal abstract bool ValueEquals(BehaviorBlackboardValue other);

    internal static bool TryCompare(BehaviorBlackboardValue left, BehaviorBlackboardValue right, out int comparison)
    {
        if (left.TryGetNumber(out var leftNumber) && right.TryGetNumber(out var rightNumber))
        {
            comparison = leftNumber.CompareTo(rightNumber);
            return true;
        }

        if (left.TryCompareSameType(right, out comparison))
        {
            return true;
        }

        comparison = 0;
        return false;
    }

    internal virtual bool TryGetNumber(out double result)
    {
        switch (this)
        {
            case BehaviorBlackboardValue<byte> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<sbyte> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<short> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<ushort> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<int> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<uint> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<long> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<ulong> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<float> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<double> typed:
                result = typed.Value;
                return true;
            case BehaviorBlackboardValue<decimal> typed:
                result = (double)typed.Value;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    internal abstract bool TryCompareSameType(BehaviorBlackboardValue other, out int comparison);
}

public sealed class BehaviorBlackboardValue<TValue> : BehaviorBlackboardValue
{
    public BehaviorBlackboardValue()
    {
    }

    public BehaviorBlackboardValue(TValue? value)
    {
        Value = value;
    }

    public TValue? Value { get; private set; }

    public override Type ValueType => typeof(TValue);

    public override void OnRelease()
    {
        Value = default;
    }

    internal void SetValue(TValue? value)
    {
        Value = value;
    }

    internal override bool ValueEquals(BehaviorBlackboardValue other)
    {
        if (other is BehaviorBlackboardValue<TValue> typed)
        {
            return EqualityComparer<TValue>.Default.Equals(Value, typed.Value);
        }

        return false;
    }

    internal override bool TryCompareSameType(BehaviorBlackboardValue other, out int comparison)
    {
        if (other is not BehaviorBlackboardValue<TValue> typed)
        {
            comparison = 0;
            return false;
        }

        try
        {
            comparison = Comparer<TValue>.Default.Compare(Value, typed.Value);
            return true;
        }
        catch (ArgumentException)
        {
            comparison = 0;
            return false;
        }
    }
}

internal sealed class BehaviorBlackboardValuePool
{
    private readonly Dictionary<Type, IValuePool> _pools = [];

    public BehaviorBlackboardValue Rent<TValue>(TValue? value)
    {
        return GetPool<TValue>().Rent(value);
    }

    public void Release(BehaviorBlackboardValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (_pools.TryGetValue(value.ValueType, out var pool))
        {
            pool.Release(value);
        }
    }

    public void Clear()
    {
        foreach (var pool in _pools.Values)
        {
            pool.Clear();
        }

        _pools.Clear();
    }

    private ValuePool<TValue> GetPool<TValue>()
    {
        var type = typeof(TValue);
        if (_pools.TryGetValue(type, out var pool))
        {
            return (ValuePool<TValue>)pool;
        }

        var typedPool = new ValuePool<TValue>();
        _pools.Add(type, typedPool);
        return typedPool;
    }

    private interface IValuePool
    {
        void Release(BehaviorBlackboardValue value);

        void Clear();
    }

    private sealed class ValuePool<TValue> : IValuePool
    {
        private readonly Stack<BehaviorBlackboardValue<TValue>> _available = [];

        public BehaviorBlackboardValue<TValue> Rent(TValue? value)
        {
            var item = _available.Count > 0 ? _available.Pop() : new BehaviorBlackboardValue<TValue>();
            item.OnAcquire();
            item.SetValue(value);
            return item;
        }

        public void Release(BehaviorBlackboardValue value)
        {
            var typed = (BehaviorBlackboardValue<TValue>)value;
            typed.OnRelease();
            _available.Push(typed);
        }

        public void Clear()
        {
            _available.Clear();
        }
    }
}
