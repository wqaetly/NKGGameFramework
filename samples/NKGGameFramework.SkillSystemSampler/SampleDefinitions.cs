using NKGGameFramework.Gameplay;

namespace NKGGameFramework.SkillSystemSampler;

internal static class SampleDefinitions
{
    public static SkillDefinition CreateTriFireballSkill(BuffDefinition burning)
    {
        // 技能定义只声明数据和行为树，不直接写“发射几枚火球”的流程代码。
        return new SkillDefinition
        {
            Id = SampleConstants.SkillId,
            DisplayName = "三段式火球",
            Description = "每次释放会多发射一枚由行为树驱动的火球，最多三枚。",
            ReleaseMode = SkillReleaseMode.Target,
            Cooldowns = { [1] = TimeSpan.FromSeconds(0.75) },
            ExecutionTree = new BehaviorTreeDefinition
            {
                Root = new BehaviorNodeDefinition
                {
                    Type = BehaviorNodeTypes.Sequence,
                    Children =
                    {
                        Action("prepare_fireball_cast"),
                        Action("play_animation", ("name", "火球施法动画")),
                        Wait(0.20),
                        Fireball(1, burning),
                        Wait(0.12),
                        OptionalFireball(2, burning),
                        Wait(0.12),
                        OptionalFireball(3, burning),
                        Action("play_fx", ("name", "火球结束特效")),
                    },
                },
            },
        };
    }

    public static BuffDefinition CreateBurningBuff()
    {
        // 灼烧的周期伤害也放进行为树；增益更新系统只负责推进生命周期。
        return new BuffDefinition
        {
            Id = "burning",
            DisplayName = "灼烧",
            EffectKey = "burn_lifecycle",
            Kind = BuffKind.Debuff,
            DamageKind = BuffDamageKind.Sustain | BuffDamageKind.Magical | BuffDamageKind.Skill,
            Duration = TimeSpan.FromSeconds(4),
            MaxStacks = SampleConstants.MaxProjectiles,
            ValuesByLevel = { [1] = 12 },
            ExecutionTree = new BehaviorTreeDefinition
            {
                Loop = true,
                Root = new BehaviorNodeDefinition
                {
                    Type = BehaviorNodeTypes.Sequence,
                    Children =
                    {
                        Wait(1.0),
                        Action("burn_tick_damage"),
                    },
                },
            },
        };
    }

    // 技能树负责施法编排：段数准备、动画、等待、火球飞行和第二/第三发可选分支都在数据节点中表达。
    private static BehaviorNodeDefinition OptionalFireball(int index, BuffDefinition burning)
    {
        return new BehaviorNodeDefinition
        {
            Type = BehaviorNodeTypes.Selector,
            Children =
            {
                new BehaviorNodeDefinition
                {
                    Type = BehaviorNodeTypes.BlackboardCondition,
                    BlackboardKey = SampleConstants.ProjectileCountKey,
                    Operator = BehaviorConditionOperator.GreaterOrEqual,
                    Value = index,
                    Children = { Fireball(index, burning) },
                },
                Action("noop"),
            },
        };
    }

    private static BehaviorNodeDefinition Fireball(int index, BuffDefinition burning)
    {
        return Action(
            "launch_fireball",
            burning,
            ("index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("flight", "0.20"),
            ("impact", "18"));
    }

    private static BehaviorNodeDefinition Action(
        string key,
        params (string Key, string Value)[] parameters)
    {
        return Action(key, buff: null, parameters);
    }

    private static BehaviorNodeDefinition Action(
        string key,
        BuffDefinition? buff,
        params (string Key, string Value)[] parameters)
    {
        var node = new BehaviorNodeDefinition
        {
            Type = BehaviorNodeTypes.Action,
            ActionKey = key,
            Buff = buff,
        };

        foreach (var (parameterKey, value) in parameters)
        {
            node.Parameters[parameterKey] = value;
        }

        return node;
    }

    private static BehaviorNodeDefinition Wait(double seconds)
    {
        return new BehaviorNodeDefinition
        {
            Type = BehaviorNodeTypes.Wait,
            Duration = TimeSpan.FromSeconds(seconds),
        };
    }
}
