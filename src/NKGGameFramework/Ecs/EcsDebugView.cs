namespace NKGGameFramework.Ecs;

public sealed record EcsComponentStoreDebugView(
    Type ComponentType,
    int Count,
    IReadOnlyList<int> EntityIds);

public sealed record EcsComponentDebugView(
    Type ComponentType,
    object Value);
