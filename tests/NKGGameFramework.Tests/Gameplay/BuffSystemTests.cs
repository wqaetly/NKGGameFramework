using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.Tests.Gameplay;

public sealed class BuffSystemTests
{
    [Fact]
    public void Buffs_apply_refresh_stack_and_expire()
    {
        var calls = new List<string>();
        var effects = new BuffEffectRegistry()
            .Register("record", new DelegateBuffEffect(
                onApply: context => calls.Add($"apply:{context.Buff.Stacks}"),
                onRefresh: context => calls.Add($"refresh:{context.Buff.Stacks}"),
                onUpdate: _ => calls.Add("update"),
                onRemove: _ => calls.Add("remove")));

        using var scene = new Scene("battle");
        scene.Systems.Add(new BuffUpdateSystem(effects));

        var source = scene.CreateEntity();
        var target = scene.CreateEntity();
        var definition = new BuffDefinition
        {
            Id = "bleed",
            EffectKey = "record",
            Duration = TimeSpan.FromSeconds(1),
            MaxStacks = 3,
        };

        var apply = BuffManager.Apply(source, target, definition);
        Assert.True(apply.IsNewInstance);

        scene.Update(0.25, 0.25);

        Assert.Contains("apply:1", calls);
        Assert.Equal(BuffState.Running, apply.Instance.State);

        var refresh = BuffManager.Apply(source, target, definition);
        Assert.True(refresh.IsRefresh);
        Assert.Equal(2, refresh.Instance.Stacks);

        scene.Update(0.25, 0.25);

        Assert.Contains("refresh:2", calls);
        Assert.True(BuffManager.Has(target, "bleed"));

        scene.Update(1, 1);

        Assert.False(BuffManager.Has(target, "bleed"));
        Assert.Contains("remove", calls);
    }

    [Fact]
    public void Unique_per_source_buffs_keep_instances_separate()
    {
        using var scene = new Scene("battle");

        var firstSource = scene.CreateEntity();
        var secondSource = scene.CreateEntity();
        var target = scene.CreateEntity();
        var definition = new BuffDefinition
        {
            Id = "mark",
            UniquePerSource = true,
            Duration = null,
        };

        BuffManager.Apply(firstSource, target, definition);
        BuffManager.Apply(secondSource, target, definition);

        ref var collection = ref target.Get<BuffCollectionComponent>();
        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public void Buff_requirements_can_block_application_and_active_buffs_grant_tags()
    {
        using var scene = new Scene("battle");

        var source = scene.CreateEntity();
        var target = scene.CreateEntity()
            .Add(new GameplayTagComponent(GameplayTagContainer.From("State.Invulnerable")));
        var blocked = new BuffDefinition
        {
            Id = "burn",
            BlockedTargetTags = GameplayTagContainer.From("State.Invulnerable"),
            Tags = GameplayTagContainer.From("State.Burning"),
        };

        Assert.False(BuffManager.TryApply(source, target, blocked, out _, out var failureReason));
        Assert.Equal("Blocked gameplay tags are present.", failureReason);

        var allowed = new BuffDefinition
        {
            Id = "chill",
            Tags = GameplayTagContainer.From("State.Chilled"),
        };

        Assert.True(BuffManager.TryApply(source, target, allowed, out _, out _, stacks: 1));
        Assert.True(GameplayTagUtility.GetOwnedTags(target).HasTag(GameplayTag.From("State")));
        Assert.True(GameplayTagUtility.GetOwnedTags(target).HasTagExact(GameplayTag.From("State.Chilled")));
    }

    [Fact]
    public void Buff_query_gate_can_reject_target_tags()
    {
        using var scene = new Scene("battle");

        var source = scene.CreateEntity();
        var target = scene.CreateEntity()
            .Add(new GameplayTagComponent(GameplayTagContainer.From("State.Invulnerable")));
        var definition = new BuffDefinition
        {
            Id = "execute",
            TargetTagQuery = GameplayTagQuery.MatchNoTags(GameplayTagContainer.From("State.Invulnerable")),
        };

        var applied = BuffManager.TryApply(source, target, definition, out _, out var failureReason);

        Assert.False(applied);
        Assert.Equal("Gameplay tag query did not match.", failureReason);
    }

    [Fact]
    public void Buff_execution_tree_runs_inside_buff_lifetime()
    {
        var calls = new List<string>();
        var actions = new BehaviorActionRegistry()
            .Register("record", new DelegateBehaviorAction(context =>
            {
                calls.Add($"{context.BuffInstance?.Definition.Id}:{context.Request}");
                return BehaviorActionStatus.Success;
            }));

        using var scene = new Scene("battle");
        scene.Systems.Add(new BuffUpdateSystem(behaviorActions: actions));

        var source = scene.CreateEntity();
        var target = scene.CreateEntity();
        var definition = new BuffDefinition
        {
            Id = "delayed_mark",
            Duration = TimeSpan.FromSeconds(1),
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
                            Duration = TimeSpan.FromSeconds(0.25),
                        },
                        new BehaviorNodeDefinition
                        {
                            Type = BehaviorNodeTypes.Action,
                            ActionKey = "record",
                        },
                    },
                },
            },
        };

        BuffManager.Apply(source, target, definition);

        scene.Update(0.1, 0.1);

        Assert.Empty(calls);

        scene.Update(0.15, 0.15);

        Assert.Equal(["delayed_mark:Start"], calls);
    }

    [Fact]
    public void Buff_execution_tree_does_not_run_after_buff_expires_in_same_frame()
    {
        var calls = new List<string>();
        var actions = new BehaviorActionRegistry()
            .Register("record", new DelegateBehaviorAction(context =>
            {
                calls.Add($"{context.BuffInstance?.Definition.Id}:{context.Request}");
                return BehaviorActionStatus.Success;
            }));

        using var scene = new Scene("battle");
        scene.Systems.Add(new BuffUpdateSystem(behaviorActions: actions));

        var source = scene.CreateEntity();
        var target = scene.CreateEntity();
        var definition = new BuffDefinition
        {
            Id = "short_burn",
            Duration = TimeSpan.FromSeconds(0.1),
            ExecutionTree = new BehaviorTreeDefinition
            {
                Root = new BehaviorNodeDefinition
                {
                    Type = BehaviorNodeTypes.Action,
                    ActionKey = "record",
                },
            },
        };

        BuffManager.Apply(source, target, definition);

        scene.Update(0.1, 0.1);

        Assert.Empty(calls);
        Assert.False(BuffManager.Has(target, "short_burn"));
    }
}
