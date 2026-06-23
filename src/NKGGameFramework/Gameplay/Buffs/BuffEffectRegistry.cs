namespace NKGGameFramework.Gameplay;

public sealed class BuffEffectRegistry
{
    private readonly Dictionary<string, IBuffEffect> _effects = new(StringComparer.Ordinal);

    public BuffEffectRegistry()
    {
        Register(BuffEffectKeys.None, NullBuffEffect.Instance);
    }

    public static BuffEffectRegistry CreateDefault()
    {
        return new BuffEffectRegistry();
    }

    public BuffEffectRegistry Register(string key, IBuffEffect effect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(effect);

        _effects[key] = effect;
        return this;
    }

    public bool TryResolve(string key, out IBuffEffect effect)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = BuffEffectKeys.None;
        }

        return _effects.TryGetValue(key, out effect!);
    }

    public IBuffEffect Resolve(string key)
    {
        if (TryResolve(key, out var effect))
        {
            return effect;
        }

        throw new KeyNotFoundException($"Buff effect '{key}' is not registered.");
    }

    private sealed class NullBuffEffect : BuffEffect
    {
        public static readonly NullBuffEffect Instance = new();
    }
}
