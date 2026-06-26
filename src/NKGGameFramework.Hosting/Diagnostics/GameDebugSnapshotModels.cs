using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed record GameDebugSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<RuntimeContextDebugSnapshot> Runtimes,
    IReadOnlyList<WorldDebugSnapshot> Worlds);

public sealed record GameDebugSnapshotMessage(
    GameDebugFrameInfo Frame,
    GameDebugSnapshot Snapshot,
    GameDebugControlState Control);

public sealed record GameDebugSnapshotCaptureOptions
{
    public static GameDebugSnapshotCaptureOptions Default { get; } = new();

    public string? WorldName { get; init; }

    public string? SceneName { get; init; }

    public int? EntityId { get; init; }

    public string? ComponentTypeFullName { get; init; }

    public string? ComponentAssemblyName { get; init; }

    public int EntityOffset { get; init; }

    public int? EntityLimit { get; init; }

    public bool IncludeComponentPayloads { get; init; } = true;

    public bool IncludeStructuredComponentValues { get; init; } = true;
}

public sealed record GameDebugComponentValueSerializationOptions
{
    public static GameDebugComponentValueSerializationOptions Default { get; } = new();

    public bool IncludePayload { get; init; } = true;

    public bool IncludeStructured { get; init; } = true;

    public GameDebugStructuredComponentValueCaptureOptions StructuredCaptureOptions { get; init; } =
        GameDebugStructuredComponentValueCaptureOptions.Default;
}

public sealed record GameDebugStructuredComponentValueCaptureOptions
{
    public static GameDebugStructuredComponentValueCaptureOptions Default { get; } = new();

    public int MaxDepth { get; init; } = 6;

    public int? MaxCollectionItems { get; init; }

    public bool CaptureElementTemplate { get; init; } = true;

    public bool StopAtRuntimeReferences { get; init; } = true;
}

public sealed record RuntimeContextDebugSnapshot(
    int Index,
    bool IsDisposed,
    IReadOnlyList<ModuleDebugSnapshot> Modules,
    IReadOnlyList<ProcedureModuleDebugSnapshot> ProcedureModules);

public sealed record ModuleDebugSnapshot(
    DebugTypeInfo Type,
    int Priority,
    bool IsUpdateModule);

public sealed record ProcedureModuleDebugSnapshot(
    DebugTypeInfo Type,
    bool IsInitialized,
    string? CurrentProcedure,
    double CurrentProcedureTime,
    IReadOnlyList<ProcedureDebugSnapshot> Procedures);

public sealed record ProcedureDebugSnapshot(
    DebugTypeInfo Type,
    bool IsCurrent);

public sealed record WorldDebugSnapshot(
    string Name,
    int SceneCount,
    IReadOnlyList<SceneDebugSnapshot> Scenes);

public sealed record SceneDebugSnapshot(
    string Name,
    int EntityCount,
    IReadOnlyList<SystemDebugSnapshot> Systems,
    IReadOnlyList<ComponentStoreDebugSnapshot> ComponentStores,
    IReadOnlyList<EntityDebugSnapshot> Entities);

public sealed record SystemDebugSnapshot(
    DebugTypeInfo Type,
    int Order,
    bool Enabled);

public sealed record ComponentStoreDebugSnapshot(
    DebugTypeInfo Type,
    int Count,
    IReadOnlyList<int> EntityIds);

public sealed record EntityDebugSnapshot(
    int Id,
    int Version,
    IReadOnlyList<ComponentDebugSnapshot> Components,
    IReadOnlyList<SkillDebugSnapshot> Skills,
    IReadOnlyList<BuffDebugSnapshot> Buffs);

public sealed record ComponentDebugSnapshot(
    DebugTypeInfo Type,
    ComponentValueDebugSnapshot Value,
    ComponentGraphDebugSnapshot Graph);

public sealed record ComponentGraphDebugSnapshot(
    string Id,
    string? ParentId,
    DebugTypeInfo? ParentType,
    string? Group,
    int Order);

public sealed record ComponentValueDebugSnapshot(
    string Format,
    string? Payload,
    string? Error,
    ComponentValueDebugNode? Structured = null);

public sealed record ComponentValueDebugNode
{
    public required string Kind { get; init; }

    public string? Name { get; init; }

    public required DebugTypeInfo Type { get; init; }

    public bool Editable { get; init; }

    public string? Value { get; init; }

    public IReadOnlyList<ComponentValueDebugNode> Children { get; init; } =
        Array.Empty<ComponentValueDebugNode>();

    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

    public DebugTypeInfo? ElementType { get; init; }

    public ComponentValueDebugNode? ElementTemplate { get; init; }

    public string? Error { get; init; }
}

public sealed record SkillDebugSnapshot(
    string Id,
    string? DisplayName,
    int Level,
    string Kind,
    string ReleaseMode,
    string CostKind,
    double Cost,
    double CooldownSeconds,
    double CooldownRemainingSeconds,
    bool IsCoolingDown,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ResourceLocations,
    IReadOnlyList<string> EffectKeys);

public sealed record BuffDebugSnapshot(
    string Id,
    string? DisplayName,
    int Level,
    int Stacks,
    string State,
    string Kind,
    string EffectKey,
    double? RemainingDurationSeconds,
    EntityRefDebugSnapshot Source,
    EntityRefDebugSnapshot Target,
    IReadOnlyList<string> Tags);

public sealed record EntityRefDebugSnapshot(
    int Id,
    int Version,
    bool IsAlive);

public sealed record DebugTypeInfo(
    string Name,
    string FullName,
    string AssemblyName);
