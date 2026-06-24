using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public static class BuffManager
{
    public static BuffApplicationResult Apply(
        Entity source,
        Entity target,
        BuffDefinition definition,
        int level = 1,
        int? stacks = null)
    {
        if (!TryApply(source, target, definition, out var result, out var failureReason, level, stacks))
        {
            throw new InvalidOperationException(failureReason);
        }

        return result;
    }

    public static bool TryApply(
        Entity source,
        Entity target,
        BuffDefinition definition,
        out BuffApplicationResult result,
        out string? failureReason,
        int level = 1,
        int? stacks = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);

        if (level <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Buff level must be greater than zero.");
        }

        var actualTarget = definition.TargetKind == BuffTargetKind.Self ? source : target;
        var sourceTags = GameplayTagUtility.GetOwnedTags(source);
        var targetTags = source == actualTarget ? sourceTags : GameplayTagUtility.GetOwnedTags(actualTarget);
        if (!GameplayTagUtility.MeetsRequirements(
                sourceTags,
                definition.RequiredSourceTags,
                definition.BlockedSourceTags,
                out failureReason)
            || !GameplayTagUtility.MeetsRequirements(
                targetTags,
                definition.RequiredTargetTags,
                definition.BlockedTargetTags,
                out failureReason)
            || !GameplayTagUtility.MatchesQuery(
                sourceTags,
                definition.SourceTagQuery,
                out failureReason)
            || !GameplayTagUtility.MatchesQuery(
                targetTags,
                definition.TargetTagQuery,
                out failureReason))
        {
            result = default;
            return false;
        }

        EnsureCollection(actualTarget);

        ref var collection = ref actualTarget.Get<BuffCollectionComponent>();
        var sourceRef = source.ToRef();
        var targetRef = actualTarget.ToRef();
        var stackKey = CreateStackKey(definition, sourceRef);
        var stackAmount = stacks ?? definition.StackAmount;
        if (stackAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stacks), "Buff stacks must be greater than zero.");
        }

        foreach (var buff in collection.MutableBuffs)
        {
            if (buff.StackKey == stackKey && buff.State != BuffState.Finished)
            {
                buff.Refresh(stackAmount);
                result = new BuffApplicationResult(false, true, buff);
                failureReason = null;
                return true;
            }
        }

        var instance = new BuffInstance(definition, stackKey, sourceRef, targetRef, level, stackAmount);
        collection.MutableBuffs.Add(instance);
        result = new BuffApplicationResult(true, false, instance);
        failureReason = null;
        return true;
    }

    public static bool Remove(Entity target, string buffId)
    {
        if (!target.Has<BuffCollectionComponent>())
        {
            return false;
        }

        ref var collection = ref target.Get<BuffCollectionComponent>();
        if (!collection.TryGet(buffId, out var buff))
        {
            return false;
        }

        buff.State = BuffState.Finished;
        return true;
    }

    public static bool Has(Entity target, string buffId)
    {
        return TryGet(target, buffId, out _);
    }

    public static bool TryGet(Entity target, string buffId, out BuffInstance buff)
    {
        if (!target.Has<BuffCollectionComponent>())
        {
            buff = null!;
            return false;
        }

        ref var collection = ref target.Get<BuffCollectionComponent>();
        return collection.TryGet(buffId, out buff);
    }

    private static void EnsureCollection(Entity target)
    {
        if (!target.Has<BuffCollectionComponent>())
        {
            target.Add(new BuffCollectionComponent());
        }
    }

    private static string CreateStackKey(BuffDefinition definition, EntityRef source)
    {
        return definition.UniquePerSource
            ? $"{definition.Id}@{source.Id}:{source.Version}"
            : definition.Id;
    }
}
