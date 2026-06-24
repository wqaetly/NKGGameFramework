using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.SkillSystemSampler;

internal static class SampleCombat
{
    public static int ApplyDamage(Entity target, int rawDamage, bool trueDamage)
    {
        // 火焰伤害会受到火焰抗性影响，真实伤害直接使用原始伤害。
        var actualDamage = rawDamage;
        if (!trueDamage && target.Has<FireResistance>())
        {
            actualDamage = Math.Max(1, (int)Math.Round(
                rawDamage * target.Get<FireResistance>().FireDamageMultiplier,
                MidpointRounding.AwayFromZero));
        }

        ref var health = ref target.Get<Health>();
        health.Value = Math.Max(0, health.Value - actualDamage);
        return actualDamage;
    }

    public static void ResolveCombatants(in BehaviorActionContext context, out Entity caster, out Entity target)
    {
        if (context.Owner is not { } owner || context.Target is not { } resolvedTarget)
        {
            throw new InvalidOperationException("火球行为需要施法者和目标实体。");
        }

        caster = owner;
        target = resolvedTarget;
    }

    public static string FormatSeconds(TimeSpan? duration)
    {
        return duration is { } value ? $"{value.TotalSeconds:0.0}秒" : "永久";
    }

    public static string FormatBuffState(BuffState state)
    {
        return state switch
        {
            BuffState.Waiting => "等待生效",
            BuffState.Running => "运行中",
            BuffState.Finished => "已结束",
            BuffState.Forever => "永久",
            _ => state.ToString(),
        };
    }

    public static string FormatSkillFailure(SkillCastFailureReason reason)
    {
        return reason switch
        {
            SkillCastFailureReason.None => "无",
            SkillCastFailureReason.MissingSkillBook => "缺少技能书",
            SkillCastFailureReason.UnknownSkill => "未知技能",
            SkillCastFailureReason.PassiveOnly => "被动技能不能主动释放",
            SkillCastFailureReason.Cooldown => "技能冷却中",
            SkillCastFailureReason.TagRequirementFailed => "标签条件不满足",
            SkillCastFailureReason.CostRejected => "消耗校验失败",
            SkillCastFailureReason.MissingEffect => "缺少效果实现",
            _ => reason.ToString(),
        };
    }

    // 行为树参数来自数据定义，解析失败时使用默认值保持示例可继续运行。
    public static int ReadInt(in BehaviorActionContext context, string key, int fallback)
    {
        return context.Parameters.TryGetValue(key, out var value)
            && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public static double ReadDouble(in BehaviorActionContext context, string key, double fallback)
    {
        return context.Parameters.TryGetValue(key, out var value)
            && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public static string ReadString(in BehaviorActionContext context, string key, string fallback)
    {
        return context.Parameters.TryGetValue(key, out var value) ? value : fallback;
    }
}
