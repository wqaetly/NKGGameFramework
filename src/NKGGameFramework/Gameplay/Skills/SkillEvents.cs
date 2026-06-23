using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public readonly record struct SkillCastResult(
    bool Succeeded,
    SkillCastFailureReason FailureReason = SkillCastFailureReason.None,
    string? Message = null);

public readonly record struct SkillCastSucceeded(EntityRef Caster, EntityRef Target, string SkillId, int Level);

public readonly record struct SkillCastFailed(
    EntityRef Caster,
    EntityRef Target,
    string SkillId,
    SkillCastFailureReason Reason,
    string? Message);
