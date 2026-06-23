namespace NKGGameFramework.Gameplay;

public sealed class SkillEffectDefinition
{
    public string Key { get; init; } = SkillEffectKeys.ApplyBuff;

    public BuffDefinition? Buff { get; init; }

    public Dictionary<string, string> Parameters { get; init; } = [];
}
