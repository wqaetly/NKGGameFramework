using NKGGameFramework.Gameplay;

namespace NKGGameFramework.SkillSystemSampler;

internal static class SampleRegistries
{
    public static BehaviorActionRegistry CreateSkillBehaviorActions()
    {
        // 这套注册表只服务技能释放接口启动的技能行为树。
        return new BehaviorActionRegistry()
            .Register("prepare_fireball_cast", new PrepareFireballCastAction(SampleLog.Write))
            .Register("play_animation", new LogAction("动画", SampleLog.Write))
            .Register("play_fx", new LogAction("特效", SampleLog.Write))
            .Register("launch_fireball", new FireballHitAction(SampleLog.Write))
            .Register("noop", NoopBehaviorAction.Instance);
    }

    public static BehaviorActionRegistry CreateBuffBehaviorActions()
    {
        // 这套注册表只服务增益更新系统启动的增益行为树。
        // 它不是“归增益管”，增益更新系统只是需要它解析增益树里的行为。
        return new BehaviorActionRegistry()
            .Register("burn_tick_damage", new BurnTickDamageAction(SampleLog.Write));
    }

    public static BuffEffectRegistry CreateBuffEffects()
    {
        return new BuffEffectRegistry()
            .Register("burn_lifecycle", new DelegateBuffEffect(
                onApply: context => SampleLog.Write($"灼烧生效：层数={context.Buff.Stacks}"),
                onRefresh: context => SampleLog.Write($"灼烧刷新：层数={context.Buff.Stacks}"),
                onRemove: context => SampleLog.Write($"灼烧移除：最终层数={context.Buff.Stacks}")));
    }
}
