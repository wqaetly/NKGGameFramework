namespace NKGGameFramework.Gameplay;

[Flags]
public enum SkillKind
{
    None = 0,
    Active = 1 << 0,
    Passive = 1 << 1,
    Interruptible = 1 << 2,
    Uninterruptible = 1 << 3,
}

public enum SkillReleaseMode
{
    None,
    Range,
    Arrow,
    Target,
    Sector,
}

public enum SkillCostKind
{
    None,
    Mana,
    Health,
    Other,
}

public enum SkillCastFailureReason
{
    None,
    MissingSkillBook,
    UnknownSkill,
    PassiveOnly,
    Cooldown,
    TagRequirementFailed,
    CostRejected,
    MissingEffect,
}
