namespace NKGGameFramework.Gameplay;

public sealed class BuffDefinition
{
    private int _stackAmount = 1;
    private int _maxStacks = 1;
    private TimeSpan? _duration;

    public required string Id { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public string EffectKey { get; init; } = BuffEffectKeys.None;

    public string? SourceSkillId { get; init; }

    public BuffTargetKind TargetKind { get; init; } = BuffTargetKind.Target;

    public BuffKind Kind { get; init; } = BuffKind.Buff;

    public BuffDamageKind DamageKind { get; init; } = BuffDamageKind.None;

    public bool IsVisible { get; init; }

    public bool IsNetworkSynchronized { get; init; }

    public bool UniquePerSource { get; init; }

    public GameplayTagContainer Tags { get; init; } = new();

    public GameplayTagContainer RequiredSourceTags { get; init; } = new();

    public GameplayTagContainer BlockedSourceTags { get; init; } = new();

    public GameplayTagContainer RequiredTargetTags { get; init; } = new();

    public GameplayTagContainer BlockedTargetTags { get; init; } = new();

    public GameplayTagQuery? SourceTagQuery { get; init; }

    public GameplayTagQuery? TargetTagQuery { get; init; }

    public int StackAmount
    {
        get => _stackAmount;
        init => _stackAmount = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Stack amount must be greater than zero.");
    }

    public int MaxStacks
    {
        get => _maxStacks;
        init => _maxStacks = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Max stacks must be greater than zero.");
    }

    public TimeSpan? Duration
    {
        get => _duration;
        init
        {
            if (value is { } duration && duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Buff duration cannot be negative.");
            }

            _duration = value;
        }
    }

    public BuffStackRefreshPolicy RefreshPolicy { get; init; } = BuffStackRefreshPolicy.RefreshDuration;

    public Dictionary<int, double> ValuesByLevel { get; init; } = [];

    public Dictionary<string, double> Scalars { get; init; } = [];

    public List<string> EventKeys { get; init; } = [];

    public double GetValue(int level, double fallback = 0)
    {
        if (ValuesByLevel.Count == 0)
        {
            return fallback;
        }

        if (ValuesByLevel.TryGetValue(level, out var exact))
        {
            return exact;
        }

        var bestLevel = int.MinValue;
        var bestValue = fallback;
        foreach (var (configuredLevel, value) in ValuesByLevel)
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
