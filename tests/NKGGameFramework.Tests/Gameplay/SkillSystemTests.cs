using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.Tests.Gameplay;

public sealed class SkillSystemTests
{
    [Fact]
    public void Skill_cast_applies_buff_and_starts_cooldown()
    {
        using var scene = new Scene("battle");
        scene.Systems.Add(new SkillCooldownSystem());
        scene.Systems.Add(new BuffUpdateSystem());

        var caster = scene.CreateEntity();
        var target = scene.CreateEntity();
        var buff = new BuffDefinition
        {
            Id = "slow",
            Duration = TimeSpan.FromSeconds(2),
        };
        var skill = new SkillDefinition
        {
            Id = "frostbolt",
            Cooldowns = { [1] = TimeSpan.FromSeconds(1) },
            Effects =
            {
                new SkillEffectDefinition
                {
                    Buff = buff,
                },
            },
        };

        var slot = SkillManager.Learn(caster, skill);

        var firstCast = SkillManager.TryCast(scene, caster, "frostbolt", target);

        Assert.True(firstCast.Succeeded);
        Assert.True(BuffManager.Has(target, "slow"));
        Assert.True(slot.IsCoolingDown);

        var secondCast = SkillManager.TryCast(scene, caster, "frostbolt", target);

        Assert.False(secondCast.Succeeded);
        Assert.Equal(SkillCastFailureReason.Cooldown, secondCast.FailureReason);

        scene.Update(1, 1);

        Assert.False(slot.IsCoolingDown);

        var thirdCast = SkillManager.TryCast(scene, caster, "frostbolt", target);

        Assert.True(thirdCast.Succeeded);
    }

    [Fact]
    public void Skill_cast_fails_when_blocked_caster_tag_is_present()
    {
        using var scene = new Scene("battle");

        var caster = scene.CreateEntity()
            .Add(new GameplayTagComponent(GameplayTagContainer.From("State.Silenced")));
        var target = scene.CreateEntity();
        var skill = new SkillDefinition
        {
            Id = "fireball",
            BlockedCasterTags = GameplayTagContainer.From("State.Silenced"),
        };

        SkillManager.Learn(caster, skill);

        var result = SkillManager.TryCast(scene, caster, "fireball", target);

        Assert.False(result.Succeeded);
        Assert.Equal(SkillCastFailureReason.TagRequirementFailed, result.FailureReason);
        Assert.Equal("Blocked gameplay tags are present.", result.Message);
    }

    [Fact]
    public void Skill_cast_uses_query_gates_for_caster_and_target()
    {
        using var scene = new Scene("battle");

        var caster = scene.CreateEntity()
            .Add(new GameplayTagComponent(GameplayTagContainer.From("Unit.Hero.Mage")));
        var target = scene.CreateEntity()
            .Add(new GameplayTagComponent(GameplayTagContainer.From("Team.Enemy")));
        var skill = new SkillDefinition
        {
            Id = "arcane_bolt",
            CasterTagQuery = new GameplayTagQuery(GameplayTagQueryExpression.AllExprMatch(
                GameplayTagQueryExpression.AnyTagsMatch(GameplayTagContainer.From("Unit.Hero")),
                GameplayTagQueryExpression.NoTagsMatch(GameplayTagContainer.From("State.Silenced")))),
            TargetTagQuery = GameplayTagQuery.MatchAllTags(GameplayTagContainer.From("Team.Enemy")),
        };

        SkillManager.Learn(caster, skill);

        var result = SkillManager.TryCast(scene, caster, "arcane_bolt", target);

        Assert.True(result.Succeeded);

        caster.Get<GameplayTagComponent>().Tags.AddTag(GameplayTag.From("State.Silenced"));

        var blocked = SkillManager.TryCast(scene, caster, "arcane_bolt", target);

        Assert.False(blocked.Succeeded);
        Assert.Equal(SkillCastFailureReason.TagRequirementFailed, blocked.FailureReason);
        Assert.Equal("Gameplay tag query did not match.", blocked.Message);
    }

    [Fact]
    public void Behavior_tree_skill_waits_then_applies_buff()
    {
        using var scene = new Scene("battle");
        scene.Systems.Add(new BehaviorTreeUpdateSystem());

        var caster = scene.CreateEntity();
        var target = scene.CreateEntity();
        var buff = new BuffDefinition
        {
            Id = "rooted",
            Duration = TimeSpan.FromSeconds(1),
        };
        var skill = new SkillDefinition
        {
            Id = "binding_roots",
            ExecutionTree = new BehaviorTreeDefinition
            {
                Root = new BehaviorNodeDefinition
                {
                    Type = BehaviorNodeTypes.Sequence,
                    Children =
                    {
                        new BehaviorNodeDefinition
                        {
                            Type = BehaviorNodeTypes.Wait,
                            Duration = TimeSpan.FromSeconds(0.5),
                        },
                        new BehaviorNodeDefinition
                        {
                            Type = BehaviorNodeTypes.Action,
                            ActionKey = BehaviorActionKeys.ApplyBuff,
                            Buff = buff,
                        },
                    },
                },
            },
        };

        SkillManager.Learn(caster, skill);

        var result = SkillManager.TryCast(scene, caster, "binding_roots", target);

        Assert.True(result.Succeeded);
        Assert.False(BuffManager.Has(target, "rooted"));

        scene.Update(0.25, 0.25);

        Assert.False(BuffManager.Has(target, "rooted"));

        scene.Update(0.25, 0.25);

        Assert.True(BuffManager.Has(target, "rooted"));
    }

    [Fact]
    public void Behavior_tree_skill_uses_registered_presentation_actions()
    {
        var calls = new List<string>();
        var actions = new BehaviorActionRegistry()
            .Register("play_animation", new DelegateBehaviorAction(_ =>
            {
                calls.Add("animation");
                return BehaviorActionStatus.Success;
            }))
            .Register("play_fx", new DelegateBehaviorAction(_ =>
            {
                calls.Add("fx");
                return BehaviorActionStatus.Success;
            }));

        using var scene = new Scene("battle");
        var caster = scene.CreateEntity();
        var target = scene.CreateEntity();
        var skill = new SkillDefinition
        {
            Id = "slash",
            ExecutionTree = new BehaviorTreeDefinition
            {
                Root = new BehaviorNodeDefinition
                {
                    Type = BehaviorNodeTypes.Sequence,
                    Children =
                    {
                        new BehaviorNodeDefinition
                        {
                            Type = BehaviorNodeTypes.Action,
                            ActionKey = "play_animation",
                        },
                        new BehaviorNodeDefinition
                        {
                            Type = BehaviorNodeTypes.Action,
                            ActionKey = "play_fx",
                        },
                    },
                },
            },
        };

        SkillManager.Learn(caster, skill);

        var result = SkillManager.TryCast(scene, caster, "slash", target, behaviorActions: actions);

        Assert.True(result.Succeeded);
        Assert.Equal(["animation", "fx"], calls);
    }
}
