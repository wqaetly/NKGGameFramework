using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public interface IGameplayTagAsset
{
    void GetOwnedGameplayTags(GameplayTagContainer tagContainer);
}

public static class GameplayTagAssetExtensions
{
    public static GameplayTagContainer GetOwnedGameplayTags(this IGameplayTagAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var tags = new GameplayTagContainer();
        asset.GetOwnedGameplayTags(tags);
        return tags;
    }

    public static bool HasMatchingGameplayTag(this IGameplayTagAsset asset, GameplayTag tagToCheck)
    {
        return asset.GetOwnedGameplayTags().HasTag(tagToCheck);
    }

    public static bool HasAllMatchingGameplayTags(this IGameplayTagAsset asset, GameplayTagContainer tagContainer)
    {
        return asset.GetOwnedGameplayTags().HasAll(tagContainer);
    }

    public static bool HasAnyMatchingGameplayTags(this IGameplayTagAsset asset, GameplayTagContainer tagContainer)
    {
        return asset.GetOwnedGameplayTags().HasAny(tagContainer);
    }
}

public sealed class EntityGameplayTagAsset(Entity entity) : IGameplayTagAsset
{
    public Entity Entity { get; } = entity;

    public void GetOwnedGameplayTags(GameplayTagContainer tagContainer)
    {
        ArgumentNullException.ThrowIfNull(tagContainer);
        tagContainer.AppendTags(GameplayTagUtility.GetOwnedTags(Entity));
    }
}
