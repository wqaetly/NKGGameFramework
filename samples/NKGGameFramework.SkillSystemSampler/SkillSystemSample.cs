using NKGGameFramework.Core;
using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.SkillSystemSampler;

internal static class SkillSystemSample
{
    public static World Run()
    {
        SampleLog.Write("技能系统示例开始");

        var world = new World("skill-system-sampler");
        var scene = world.CreateScene("skill-sampler");
        var burning = SampleDefinitions.CreateBurningBuff();
        var skillActions = SampleRegistries.CreateSkillBehaviorActions();
        var buffActions = SampleRegistries.CreateBuffBehaviorActions();
        var buffEffects = SampleRegistries.CreateBuffEffects();

        // 系统顺序体现运行时职责：先推进冷却，再推进技能树，最后推进增益生命周期和增益树。
        scene.Systems.Add(new SkillCooldownSystem(order: 0));
        scene.Systems.Add(new BehaviorTreeUpdateSystem(order: 10));
        scene.Systems.Add(new BuffUpdateSystem(buffEffects, buffActions, order: 20));

        // 施法者保存连段状态；目标保存生命和火焰抗性，用来展示真实伤害绕过抗性。
        var caster = scene.CreateEntity()
            .Add(new Health(100))
            .Add(new FireballComboComponent(nextProjectileCount: 1, maxProjectileCount: SampleConstants.MaxProjectiles));

        var enemy = scene.CreateEntity()
            .Add(new Health(160))
            .Add(new FireResistance(fireDamageMultiplier: 0.5));

        SkillManager.Learn(caster, SampleDefinitions.CreateTriFireballSkill(burning));
        SampleLog.Write("法师学习三段式火球；敌人初始生命=160，火焰伤害承受倍率=50%");

        RunCasts(scene, caster, enemy, skillActions);

        var finalHealth = enemy.Get<Health>().Value;
        var finalBurn = BuffManager.TryGet(enemy, "burning", out var burn)
            ? $"{burn.Stacks}层，状态={SampleCombat.FormatBuffState(burn.State)}，剩余={SampleCombat.FormatSeconds(burn.RemainingDuration)}"
            : "无";
        SampleLog.Write($"最终敌人生命={finalHealth}，灼烧={finalBurn}");
        SampleLog.Write("技能系统示例结束");
        return world;
    }

    private static void RunCasts(Scene scene, Entity caster, Entity enemy, BehaviorActionRegistry skillActions)
    {
        // 三次释放分别展示 1/2/3 发火球，间隔留给上一棵技能树完成。
        var castTimes = new[] { 0.0, 1.2, 2.4 };
        var nextCast = 0;
        var elapsed = 0.0;
        var time = GameFrameTime.Zero;

        while (elapsed <= 8.0)
        {
            if (nextCast < castTimes.Length && elapsed + 0.0001 >= castTimes[nextCast])
            {
                var castNumber = nextCast + 1;
                var result = SkillManager.TryCast(scene, caster, SampleConstants.SkillId, enemy, behaviorActions: skillActions);
                SampleLog.Write(result.Succeeded
                    ? $"第 {castNumber} 次施法成功：时间={elapsed:0.0}秒"
                    : $"第 {castNumber} 次施法失败：时间={elapsed:0.0}秒，原因={SampleCombat.FormatSkillFailure(result.FailureReason)}");
                nextCast++;
            }

            // 推进场景会同时驱动冷却、技能行为树和增益行为树。
            time = GameFrameTime.Advance(time, SampleConstants.FrameDelta, SampleConstants.FrameDelta);
            scene.Update(in time);
            elapsed += SampleConstants.FrameDelta;
        }
    }
}
