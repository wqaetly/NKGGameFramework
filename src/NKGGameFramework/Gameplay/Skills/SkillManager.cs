using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public static class SkillManager
{
    public static SkillSlot Learn(Entity owner, SkillDefinition definition, int level = 1)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);

        EnsureSkillBook(owner);

        ref var book = ref owner.Get<SkillBookComponent>();
        if (book.MutableSkills.TryGetValue(definition.Id, out var slot))
        {
            slot.Refresh(definition, level);
            return slot;
        }

        slot = new SkillSlot(definition, level);
        book.MutableSkills.Add(definition.Id, slot);
        return slot;
    }

    public static bool TryGet(Entity owner, string skillId, out SkillSlot slot)
    {
        if (!owner.Has<SkillBookComponent>())
        {
            slot = null!;
            return false;
        }

        ref var book = ref owner.Get<SkillBookComponent>();
        return book.TryGet(skillId, out slot);
    }

    public static SkillCastResult TryCast(
        Scene scene,
        Entity caster,
        string skillId,
        Entity target,
        SkillEffectRegistry? effects = null,
        ISkillCostPolicy? costPolicy = null,
        BehaviorActionRegistry? behaviorActions = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);

        if (!caster.Has<SkillBookComponent>())
        {
            return Fail(scene, caster, target, skillId, SkillCastFailureReason.MissingSkillBook);
        }

        ref var book = ref caster.Get<SkillBookComponent>();
        if (!book.TryGet(skillId, out var slot))
        {
            return Fail(scene, caster, target, skillId, SkillCastFailureReason.UnknownSkill);
        }

        if ((slot.Definition.Kind & SkillKind.Active) == 0 && (slot.Definition.Kind & SkillKind.Passive) != 0)
        {
            return Fail(scene, caster, target, skillId, SkillCastFailureReason.PassiveOnly);
        }

        if (slot.IsCoolingDown)
        {
            return Fail(scene, caster, target, skillId, SkillCastFailureReason.Cooldown);
        }

        var casterTags = GameplayTagUtility.GetOwnedTags(caster);
        var targetTags = caster == target ? casterTags : GameplayTagUtility.GetOwnedTags(target);
        if (!GameplayTagUtility.MeetsRequirements(
                casterTags,
                slot.Definition.RequiredCasterTags,
                slot.Definition.BlockedCasterTags,
                out var tagFailureReason)
            || !GameplayTagUtility.MeetsRequirements(
                targetTags,
                slot.Definition.RequiredTargetTags,
                slot.Definition.BlockedTargetTags,
                out tagFailureReason)
            || !GameplayTagUtility.MatchesQuery(
                casterTags,
                slot.Definition.CasterTagQuery,
                out tagFailureReason)
            || !GameplayTagUtility.MatchesQuery(
                targetTags,
                slot.Definition.TargetTagQuery,
                out tagFailureReason))
        {
            return Fail(scene, caster, target, skillId, SkillCastFailureReason.TagRequirementFailed, tagFailureReason);
        }

        effects ??= SkillEffectRegistry.CreateDefault();
        behaviorActions ??= BehaviorActionRegistry.CreateDefault(effects);

        if (slot.Definition.ExecutionTree is null)
        {
            var resolvedEffects = new List<(ISkillEffect Effect, SkillEffectDefinition Definition)>(slot.Definition.Effects.Count);
            foreach (var effectDefinition in slot.Definition.Effects)
            {
                if (!effects.TryResolve(effectDefinition.Key, out var skillEffect))
                {
                    return Fail(scene, caster, target, skillId, SkillCastFailureReason.MissingEffect, effectDefinition.Key);
                }

                resolvedEffects.Add((skillEffect, effectDefinition));
            }

            return TryPayAndExecuteLegacy(scene, caster, target, skillId, slot, costPolicy, resolvedEffects);
        }

        if (!slot.Definition.ExecutionTree.TryValidate(behaviorActions, out var missingActionKey))
        {
            return Fail(scene, caster, target, skillId, SkillCastFailureReason.MissingEffect, missingActionKey);
        }

        return TryPayAndStartBehaviorTree(scene, caster, target, skillId, slot, costPolicy, behaviorActions);
    }

    private static SkillCastResult TryPayAndExecuteLegacy(
        Scene scene,
        Entity caster,
        Entity target,
        string skillId,
        SkillSlot slot,
        ISkillCostPolicy? costPolicy,
        List<(ISkillEffect Effect, SkillEffectDefinition Definition)> resolvedEffects)
    {
        costPolicy ??= AllowAllSkillCostPolicy.Instance;
        var costContext = new SkillCostContext(scene, caster, target, slot);
        if (!costPolicy.CanPay(costContext, out var reason))
        {
            return Fail(scene, caster, target, skillId, SkillCastFailureReason.CostRejected, reason);
        }

        costPolicy.Pay(costContext);

        var executionContext = new SkillExecutionContext(scene, caster, target, slot);
        foreach (var (effect, effectDefinition) in resolvedEffects)
        {
            effect.Execute(executionContext, effectDefinition);
        }

        slot.StartCooldown();
        scene.Events.Publish(new SkillCastSucceeded(caster.ToRef(), target.ToRef(), skillId, slot.Level));
        return new SkillCastResult(true);
    }

    private static SkillCastResult TryPayAndStartBehaviorTree(
        Scene scene,
        Entity caster,
        Entity target,
        string skillId,
        SkillSlot slot,
        ISkillCostPolicy? costPolicy,
        BehaviorActionRegistry behaviorActions)
    {
        costPolicy ??= AllowAllSkillCostPolicy.Instance;
        var costContext = new SkillCostContext(scene, caster, target, slot);
        if (!costPolicy.CanPay(costContext, out var reason))
        {
            return Fail(scene, caster, target, skillId, SkillCastFailureReason.CostRejected, reason);
        }

        costPolicy.Pay(costContext);

        var behaviorContext = new BehaviorTreeContext(scene, caster, target, slot);
        var instance = slot.Definition.ExecutionTree!.CreateInstance(behaviorActions, behaviorContext);
        BehaviorTreeManager.Start(caster, instance);

        slot.StartCooldown();
        scene.Events.Publish(new SkillCastSucceeded(caster.ToRef(), target.ToRef(), skillId, slot.Level));
        return new SkillCastResult(true);
    }

    private static SkillCastResult Fail(
        Scene scene,
        Entity caster,
        Entity target,
        string skillId,
        SkillCastFailureReason reason,
        string? message = null)
    {
        scene.Events.Publish(new SkillCastFailed(caster.ToRef(), target.ToRef(), skillId, reason, message));
        return new SkillCastResult(false, reason, message);
    }

    private static void EnsureSkillBook(Entity owner)
    {
        if (!owner.Has<SkillBookComponent>())
        {
            owner.Add(new SkillBookComponent());
        }
    }
}
