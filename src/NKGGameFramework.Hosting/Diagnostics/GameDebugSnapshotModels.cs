namespace NKGGameFramework.Hosting.Diagnostics;

public sealed record GameDebugSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<RuntimeContextDebugSnapshot> Runtimes,
    IReadOnlyList<WorldDebugSnapshot> Worlds);

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
    ComponentValueDebugSnapshot Value);

public sealed record ComponentValueDebugSnapshot(
    string Format,
    string? Payload,
    string? Error);

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
