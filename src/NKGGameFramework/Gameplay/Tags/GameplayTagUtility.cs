using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public static class GameplayTagUtility
{
    public static GameplayTagContainer GetOwnedTags(Entity entity)
    {
        var result = new GameplayTagContainer();
        AppendOwnedTags(entity, result);
        return result;
    }

    public static void AppendOwnedTags(Entity entity, GameplayTagContainer result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (entity.Has<GameplayTagComponent>())
        {
            ref var tagComponent = ref entity.Get<GameplayTagComponent>();
            result.AppendTags(tagComponent.Tags);
        }

        if (entity.Has<BuffCollectionComponent>())
        {
            ref var buffs = ref entity.Get<BuffCollectionComponent>();
            foreach (var buff in buffs.Buffs)
            {
                if (buff.State != BuffState.Finished)
                {
                    result.AppendTags(buff.Definition.Tags);
                }
            }
        }
    }

    public static bool MeetsRequirements(
        GameplayTagContainer ownedTags,
        GameplayTagContainer requiredTags,
        GameplayTagContainer blockedTags,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(ownedTags);
        ArgumentNullException.ThrowIfNull(requiredTags);
        ArgumentNullException.ThrowIfNull(blockedTags);

        if (!requiredTags.IsEmpty && !ownedTags.HasAll(requiredTags))
        {
            failureReason = "Required gameplay tags are missing.";
            return false;
        }

        if (!blockedTags.IsEmpty && ownedTags.HasAny(blockedTags))
        {
            failureReason = "Blocked gameplay tags are present.";
            return false;
        }

        failureReason = null;
        return true;
    }

    public static bool MatchesQuery(
        GameplayTagContainer ownedTags,
        GameplayTagQuery? query,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(ownedTags);

        if (query is null || query.IsEmpty || query.Matches(ownedTags))
        {
            failureReason = null;
            return true;
        }

        failureReason = "Gameplay tag query did not match.";
        return false;
    }

    public static bool HasMatchingGameplayTag(Entity entity, GameplayTag tagToCheck)
    {
        return GetOwnedTags(entity).HasTag(tagToCheck);
    }

    public static bool HasAllMatchingGameplayTags(Entity entity, GameplayTagContainer tagContainer)
    {
        return GetOwnedTags(entity).HasAll(tagContainer);
    }

    public static bool HasAnyMatchingGameplayTags(Entity entity, GameplayTagContainer tagContainer)
    {
        return GetOwnedTags(entity).HasAny(tagContainer);
    }
}
