namespace NKGGameFramework.Gameplay;

public sealed class SkillEffectRegistry
{
    private readonly Dictionary<string, ISkillEffect> _effects = new(StringComparer.Ordinal);

    public SkillEffectRegistry()
    {
        Register(SkillEffectKeys.ApplyBuff, ApplyBuffSkillEffect.Instance);
    }

    public static SkillEffectRegistry CreateDefault()
    {
        return new SkillEffectRegistry();
    }

    public SkillEffectRegistry Register(string key, ISkillEffect effect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(effect);

        _effects[key] = effect;
        return this;
    }

    public bool TryResolve(string key, out ISkillEffect effect)
    {
        return _effects.TryGetValue(key, out effect!);
    }
}
