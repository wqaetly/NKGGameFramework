using NKGGameFramework.Core;
using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugSnapshotProvider : IGameDebugSnapshotProvider
{
    private readonly GameDebugSession _session;
    private readonly IGameDebugComponentValueSerializer _componentValueSerializer;

    public GameDebugSnapshotProvider(
        GameDebugSession session,
        IGameDebugComponentValueSerializer componentValueSerializer)
    {
        _session = session;
        _componentValueSerializer = componentValueSerializer;
    }

    public GameDebugSnapshot Capture(GameDebugSnapshotCaptureOptions? options = null)
    {
        options ??= GameDebugSnapshotCaptureOptions.Default;

        return new GameDebugSnapshot(
            DateTimeOffset.UtcNow,
            _session.GetRuntimeContexts().Select(CaptureRuntime).ToArray(),
            _session.GetWorlds()
                .Where(world => MatchesWorld(world, options))
                .Select(world => CaptureWorld(world, options))
                .Where(world => string.IsNullOrWhiteSpace(options.SceneName) || world.SceneCount > 0)
                .ToArray());
    }

    private RuntimeContextDebugSnapshot CaptureRuntime(RuntimeContext runtime, int index)
    {
        var modules = runtime.Modules
            .Select(static module => new ModuleDebugSnapshot(
                DebugSnapshotTypeNames.Create(module.GetType()),
                module.Priority,
                module is IUpdateModule))
            .ToArray();

        var procedureModules = runtime.Modules
            .OfType<ProcedureModule>()
            .Select(CaptureProcedureModule)
            .ToArray();

        return new RuntimeContextDebugSnapshot(
            index,
            runtime.IsDisposed,
            modules,
            procedureModules);
    }

    private ProcedureModuleDebugSnapshot CaptureProcedureModule(ProcedureModule module)
    {
        module.TryGetCurrentProcedure(out var currentProcedure);

        return new ProcedureModuleDebugSnapshot(
            DebugSnapshotTypeNames.Create(module.GetType()),
            module.IsProcedureInitialized,
            currentProcedure?.GetType().Name,
            module.IsProcedureInitialized ? module.CurrentProcedureTime : 0,
            module.Procedures
                .Select(procedure => new ProcedureDebugSnapshot(
                    DebugSnapshotTypeNames.Create(procedure.GetType()),
                    ReferenceEquals(procedure, currentProcedure)))
                .ToArray());
    }

    private WorldDebugSnapshot CaptureWorld(World world, GameDebugSnapshotCaptureOptions options)
    {
        var scenes = world.Scenes
            .Where(scene => MatchesScene(scene, options))
            .Select(scene => CaptureScene(scene, options))
            .ToArray();

        return new WorldDebugSnapshot(
            world.Name,
            scenes.Length,
            scenes);
    }

    private SceneDebugSnapshot CaptureScene(Scene scene, GameDebugSnapshotCaptureOptions options)
    {
        var entities = scene.Entities
            .Where(entity => MatchesEntity(entity, options));
        if (options.EntityId is null)
        {
            if (options.EntityOffset > 0)
            {
                entities = entities.Skip(options.EntityOffset);
            }

            if (options.EntityLimit is { } limit)
            {
                entities = entities.Take(limit);
            }
        }

        var valueOptions = new GameDebugComponentValueSerializationOptions
        {
            IncludePayload = options.IncludeComponentPayloads,
            IncludeStructured = options.IncludeStructuredComponentValues,
        };

        return new SceneDebugSnapshot(
            scene.Name,
            scene.EntityCount,
            scene.Systems.Systems.Select(CaptureSystem).ToArray(),
            scene.ComponentStores.Select(CaptureComponentStore).ToArray(),
            entities.Select(entity => CaptureEntity(scene, entity, valueOptions)).ToArray());
    }

    private static SystemDebugSnapshot CaptureSystem(ISystem system)
    {
        return new SystemDebugSnapshot(
            DebugSnapshotTypeNames.Create(system.GetType()),
            system.Order,
            system.Enabled);
    }

    private static ComponentStoreDebugSnapshot CaptureComponentStore(EcsComponentStoreDebugView store)
    {
        return new ComponentStoreDebugSnapshot(
            DebugSnapshotTypeNames.Create(store.ComponentType),
            store.Count,
            store.EntityIds);
    }

    private EntityDebugSnapshot CaptureEntity(
        Scene scene,
        Entity entity,
        GameDebugComponentValueSerializationOptions valueOptions)
    {
        var components = scene.GetComponents(entity);
        var componentTypes = components
            .Select(static component => component.ComponentType)
            .ToHashSet();

        return new EntityDebugSnapshot(
            entity.Id.Value,
            entity.Version,
            components
                .Select(component => new ComponentDebugSnapshot(
                    DebugSnapshotTypeNames.Create(component.ComponentType),
                    _componentValueSerializer.Serialize(component.Value, valueOptions),
                    CaptureComponentGraph(component.ComponentType, componentTypes)))
                .ToArray(),
            CaptureSkills(components),
            CaptureBuffs(components));
    }

    private static bool MatchesWorld(World world, GameDebugSnapshotCaptureOptions options)
    {
        return string.IsNullOrWhiteSpace(options.WorldName)
            || StringComparer.Ordinal.Equals(world.Name, options.WorldName);
    }

    private static bool MatchesScene(Scene scene, GameDebugSnapshotCaptureOptions options)
    {
        return string.IsNullOrWhiteSpace(options.SceneName)
            || StringComparer.Ordinal.Equals(scene.Name, options.SceneName);
    }

    private static bool MatchesEntity(Entity entity, GameDebugSnapshotCaptureOptions options)
    {
        return options.EntityId is null || entity.Id.Value == options.EntityId.Value;
    }

    private static ComponentGraphDebugSnapshot CaptureComponentGraph(Type componentType, ISet<Type> componentTypes)
    {
        var graph = componentType.GetCustomAttributes(typeof(ComponentGraphAttribute), inherit: false)
            .OfType<ComponentGraphAttribute>()
            .FirstOrDefault();

        var parentType = graph?.Parent;
        var parentId = parentType is not null && componentTypes.Contains(parentType)
            ? CreateComponentGraphId(parentType)
            : null;

        return new ComponentGraphDebugSnapshot(
            CreateComponentGraphId(componentType),
            parentId,
            parentType is null ? null : DebugSnapshotTypeNames.Create(parentType),
            graph?.Group,
            graph?.Order ?? 0);
    }

    private static string CreateComponentGraphId(Type componentType)
    {
        var typeInfo = DebugSnapshotTypeNames.Create(componentType);
        return $"{typeInfo.AssemblyName}:{typeInfo.FullName}";
    }

    private static IReadOnlyList<SkillDebugSnapshot> CaptureSkills(IReadOnlyList<EcsComponentDebugView> components)
    {
        foreach (var component in components)
        {
            if (component.Value is not SkillBookComponent skillBook)
            {
                continue;
            }

            return skillBook.Skills.Values
                .OrderBy(static slot => slot.Definition.Id, StringComparer.Ordinal)
                .Select(static slot => new SkillDebugSnapshot(
                    slot.Definition.Id,
                    slot.Definition.DisplayName,
                    slot.Level,
                    slot.Definition.Kind.ToString(),
                    slot.Definition.ReleaseMode.ToString(),
                    slot.Definition.CostKind.ToString(),
                    slot.Definition.GetCost(slot.Level),
                    slot.Definition.GetCooldown(slot.Level).TotalSeconds,
                    slot.CooldownRemaining.TotalSeconds,
                    slot.IsCoolingDown,
                    FormatTags(slot.Definition.Tags),
                    slot.Definition.ResourceLocations.ToArray(),
                    slot.Definition.Effects.Select(static effect => effect.Key).ToArray()))
                .ToArray();
        }

        return Array.Empty<SkillDebugSnapshot>();
    }

    private static IReadOnlyList<BuffDebugSnapshot> CaptureBuffs(IReadOnlyList<EcsComponentDebugView> components)
    {
        foreach (var component in components)
        {
            if (component.Value is not BuffCollectionComponent buffCollection)
            {
                continue;
            }

            return buffCollection.Buffs
                .OrderBy(static buff => buff.Definition.Id, StringComparer.Ordinal)
                .Select(static buff => new BuffDebugSnapshot(
                    buff.Definition.Id,
                    buff.Definition.DisplayName,
                    buff.Level,
                    buff.Stacks,
                    buff.State.ToString(),
                    buff.Definition.Kind.ToString(),
                    buff.Definition.EffectKey,
                    buff.RemainingDuration?.TotalSeconds,
                    new EntityRefDebugSnapshot(buff.Source.Id, buff.Source.Version, buff.Source.IsAlive),
                    new EntityRefDebugSnapshot(buff.Target.Id, buff.Target.Version, buff.Target.IsAlive),
                    FormatTags(buff.Definition.Tags)))
                .ToArray();
        }

        return Array.Empty<BuffDebugSnapshot>();
    }

    private static IReadOnlyList<string> FormatTags(GameplayTagContainer tags)
    {
        return tags.Tags
            .Select(static tag => tag.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
