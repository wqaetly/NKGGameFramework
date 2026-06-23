namespace NKGGameFramework.Gameplay;

public sealed class SkillSlot
{
    internal SkillSlot(SkillDefinition definition, int level)
    {
        Refresh(definition, level);
    }

    public SkillDefinition Definition { get; private set; } = null!;

    public int Level { get; private set; }

    public TimeSpan CooldownRemaining { get; private set; }

    public bool IsCoolingDown => CooldownRemaining > TimeSpan.Zero;

    public void SetLevel(int level)
    {
        if (level <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Skill level must be greater than zero.");
        }

        Level = level;
    }

    internal void Refresh(SkillDefinition definition, int level)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);

        Definition = definition;
        SetLevel(level);
    }

    internal void StartCooldown()
    {
        CooldownRemaining = Definition.GetCooldown(Level);
    }

    internal void Tick(TimeSpan deltaTime)
    {
        if (CooldownRemaining <= TimeSpan.Zero)
        {
            return;
        }

        CooldownRemaining -= deltaTime;
        if (CooldownRemaining < TimeSpan.Zero)
        {
            CooldownRemaining = TimeSpan.Zero;
        }
    }
}
