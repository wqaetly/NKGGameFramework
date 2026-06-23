namespace NKGGameFramework.Gameplay;

public sealed class SkillDefinition
{
    public required string Id { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public SkillKind Kind { get; init; } = SkillKind.Active;

    public SkillReleaseMode ReleaseMode { get; init; } = SkillReleaseMode.None;

    public SkillCostKind CostKind { get; init; } = SkillCostKind.None;

    public List<string> ResourceLocations { get; init; } = [];

    public GameplayTagContainer Tags { get; init; } = new();

    public GameplayTagContainer RequiredCasterTags { get; init; } = new();

    public GameplayTagContainer BlockedCasterTags { get; init; } = new();

    public GameplayTagContainer RequiredTargetTags { get; init; } = new();

    public GameplayTagContainer BlockedTargetTags { get; init; } = new();

    public GameplayTagQuery? CasterTagQuery { get; init; }

    public GameplayTagQuery? TargetTagQuery { get; init; }

    public Dictionary<int, TimeSpan> Cooldowns { get; init; } = [];

    public Dictionary<int, double> Costs { get; init; } = [];

    public List<SkillEffectDefinition> Effects { get; init; } = [];

    public TimeSpan GetCooldown(int level)
    {
        return GetLeveledValue(Cooldowns, level, TimeSpan.Zero);
    }

    public double GetCost(int level)
    {
        return GetLeveledValue(Costs, level, 0);
    }

    private static TValue GetLeveledValue<TValue>(IReadOnlyDictionary<int, TValue> values, int level, TValue fallback)
    {
        if (values.Count == 0)
        {
            return fallback;
        }

        if (values.TryGetValue(level, out var exact))
        {
            return exact;
        }

        var bestLevel = int.MinValue;
        var bestValue = fallback;
        foreach (var (configuredLevel, value) in values)
        {
            if (configuredLevel <= level && configuredLevel > bestLevel)
            {
                bestLevel = configuredLevel;
                bestValue = value;
            }
        }

        return bestLevel == int.MinValue ? fallback : bestValue;
    }
}
