namespace NKGGameFramework.Ecs;

public sealed record EcsComponentStoreDebugView(
    Type ComponentType,
    int Count,
    IReadOnlyList<int> EntityIds,
    long Version = 0);

public sealed record EcsComponentStoreDumpBlock(
    Type ComponentType,
    int[] EntityIds,
    Array Values,
    long Version = 0);

public sealed record EcsComponentDebugView(
    Type ComponentType,
    object Value);

public sealed record EcsEntityComponentsDebugView(
    IReadOnlyList<Type> ComponentTypes,
    IReadOnlyList<EcsComponentDebugView> VisibleComponents);
