namespace NKGGameFramework.Gameplay;

public enum BuffKind
{
    Buff,
    Debuff,
}

public enum BuffTargetKind
{
    Self,
    Target,
}

public enum BuffState
{
    Waiting,
    Running,
    Finished,
    Forever,
}

[Flags]
public enum BuffDamageKind
{
    None = 0,
    Single = 1 << 0,
    Area = 1 << 1,
    Sustain = 1 << 2,
    Physical = 1 << 3,
    Magical = 1 << 4,
    True = 1 << 5,
    BasicAttack = 1 << 6,
    Skill = 1 << 7,
}

public enum BuffStackRefreshPolicy
{
    RefreshDuration,
    KeepDuration,
}
