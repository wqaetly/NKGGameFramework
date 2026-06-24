using NKGGameFramework.Gameplay;

namespace NKGGameFramework.SkillSystemSampler;

// 开始施法时把本次应发射的火球数量写入黑板，后续条件节点用它决定是否执行第二/第三发。
internal sealed class PrepareFireballCastAction(Action<string> log) : IBehaviorAction
{
    public BehaviorActionStatus Execute(in BehaviorActionContext context)
    {
        if (context.Request != BehaviorActionRequest.Start)
        {
            return BehaviorActionStatus.Success;
        }

        if (context.Owner is not { } caster || !caster.Has<FireballComboComponent>())
        {
            throw new InvalidOperationException("三段式火球需要施法者拥有连段组件。");
        }

        ref var combo = ref caster.Get<FireballComboComponent>();
        var projectileCount = Math.Clamp(combo.NextProjectileCount, 1, combo.MaxProjectileCount);
        context.Blackboard.Set(SampleConstants.ProjectileCountKey, projectileCount);
        combo.NextProjectileCount = Math.Min(combo.MaxProjectileCount, projectileCount + 1);

        log($"行为树准备火球段数：本次发射={projectileCount}枚");
        return BehaviorActionStatus.Success;
    }
}

// 表现层行为在这个示例里只打印日志；真实项目可以在这里接动画、特效或音效系统。
internal sealed class LogAction(string label, Action<string> log) : IBehaviorAction
{
    public BehaviorActionStatus Execute(in BehaviorActionContext context)
    {
        if (context.Request == BehaviorActionRequest.Start)
        {
            log($"{label}：{SampleCombat.ReadString(context, "name", "未命名")}");
        }

        return BehaviorActionStatus.Success;
    }
}

// 火球飞行是一个多帧行为：开始阶段发射，更新阶段等待飞行时间到达后结算命中。
internal sealed class FireballHitAction(Action<string> log) : IBehaviorAction
{
    private readonly Dictionary<BehaviorActionNode, double> _elapsedByNode = [];

    public BehaviorActionStatus Execute(in BehaviorActionContext context)
    {
        return context.Request switch
        {
            BehaviorActionRequest.Start => Start(context),
            BehaviorActionRequest.Update => Update(context),
            BehaviorActionRequest.Cancel => Cancel(context),
            _ => BehaviorActionStatus.Failure,
        };
    }

    private BehaviorActionStatus Start(in BehaviorActionContext context)
    {
        var index = SampleCombat.ReadInt(context, "index", 1);
        _elapsedByNode[context.Node] = 0;
        log($"第 {index} 发火球已发射");
        return BehaviorActionStatus.Running;
    }

    private BehaviorActionStatus Update(in BehaviorActionContext context)
    {
        if (!_elapsedByNode.TryGetValue(context.Node, out var elapsed))
        {
            return BehaviorActionStatus.Failure;
        }

        elapsed += context.DeltaTime.TotalSeconds;
        var flightTime = SampleCombat.ReadDouble(context, "flight", 0.2);
        if (elapsed < flightTime)
        {
            _elapsedByNode[context.Node] = elapsed;
            return BehaviorActionStatus.Running;
        }

        _elapsedByNode.Remove(context.Node);
        SampleCombat.ResolveCombatants(context, out var caster, out var target);

        var rawImpact = SampleCombat.ReadInt(context, "impact", 18);
        var impact = SampleCombat.ApplyDamage(target, rawImpact, trueDamage: false);
        var burn = context.Buff ?? throw new InvalidOperationException("火球命中行为需要携带灼烧定义。");
        var result = BuffManager.Apply(caster, target, burn, context.SkillSlot?.Level ?? 1);
        var index = SampleCombat.ReadInt(context, "index", 1);
        var health = target.Get<Health>().Value;
        log($"第 {index} 发火球命中：火焰伤害={impact}，灼烧层数={result.Instance.Stacks}，敌人生命={health}");
        return BehaviorActionStatus.Success;
    }

    private BehaviorActionStatus Cancel(in BehaviorActionContext context)
    {
        _elapsedByNode.Remove(context.Node);
        return BehaviorActionStatus.Failure;
    }
}

// 灼烧周期伤害由增益行为树触发，三层后切换为真实伤害。
internal sealed class BurnTickDamageAction(Action<string> log) : IBehaviorAction
{
    public BehaviorActionStatus Execute(in BehaviorActionContext context)
    {
        if (context.Request != BehaviorActionRequest.Start)
        {
            return BehaviorActionStatus.Success;
        }

        if (context.Target is not { } target || context.BuffInstance is not { } burn)
        {
            throw new InvalidOperationException("灼烧周期行为需要目标实体和增益实例。");
        }

        // 增益树负责“何时触发”，这里仅在执行时根据当前层数选择伤害规则。
        var trueDamage = burn.Stacks >= SampleConstants.MaxProjectiles;
        var rawDamage = (int)burn.Definition.GetValue(burn.Level, fallback: 12);
        var damage = SampleCombat.ApplyDamage(target, rawDamage, trueDamage);
        var health = target.Get<Health>().Value;
        log($"灼烧周期伤害：层数={burn.Stacks}，伤害类型={(trueDamage ? "真实" : "火焰")}，伤害={damage}，敌人生命={health}");
        return BehaviorActionStatus.Success;
    }
}

// 用于选择节点的兜底分支，让“未达到第二/第三发条件”也能成功跳过。
internal sealed class NoopBehaviorAction : IBehaviorAction
{
    public static readonly NoopBehaviorAction Instance = new();

    public BehaviorActionStatus Execute(in BehaviorActionContext context)
    {
        return BehaviorActionStatus.Success;
    }
}
