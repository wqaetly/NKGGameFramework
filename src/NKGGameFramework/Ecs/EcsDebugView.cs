namespace NKGGameFramework.Ecs;

public sealed record EcsComponentStoreDebugView(
    Type ComponentType,
    int Count,
    IReadOnlyList<int> EntityIds);

public sealed record EcsComponentStoreDumpBlock(
    Type ComponentType,
    int[] EntityIds,
    Array Values);

public sealed record EcsComponentDebugView(
    Type ComponentType,
    object Value);
