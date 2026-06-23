namespace NKGGameFramework.Gameplay;

public sealed record GameplayTagDefinition(
    GameplayTag Tag,
    string Source = "",
    string DevComment = "",
    bool IsExplicit = true,
    bool IsRestricted = false,
    bool AllowNonRestrictedChildren = false);

public readonly record struct GameplayTagRedirect(GameplayTag OldTag, GameplayTag NewTag);
