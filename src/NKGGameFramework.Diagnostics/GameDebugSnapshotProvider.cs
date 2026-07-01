using NKGGameFramework.Core;
using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.Diagnostics;

public sealed class GameDebugSnapshotProvider : IGameDebugSnapshotProvider
{
    private static readonly ComponentValueDebugSnapshot EmptyComponentValue = new(
        "none",
        Payload: null,
        Error: null);
    private static readonly IReadOnlyList<IGameDebugEntitySummaryProvider> EntitySummaryProviders =
    [
        new SkillDebugEntitySummaryProvider(),
        new BuffDebugEntitySummaryProvider(),
    ];

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

        var runtimes = new List<RuntimeContextDebugSnapshot>();
        if (options.IncludeRuntimeDetails)
        {
            var runtimeContexts = _session.GetRuntimeContexts();
            runtimes.Capacity = runtimeContexts.Count;
            for (var index = 0; index < runtimeContexts.Count; index++)
            {
                runtimes.Add(CaptureRuntime(runtimeContexts[index], index));
            }
        }

        var worlds = new List<WorldDebugSnapshot>();
        foreach (var world in _session.GetWorlds())
        {
            if (!MatchesWorld(world, options))
            {
                continue;
            }

            var worldSnapshot = CaptureWorld(world, options);
            if (string.IsNullOrWhiteSpace(options.SceneName) || worldSnapshot.SceneCount > 0)
            {
                worlds.Add(worldSnapshot);
            }
        }

        return new GameDebugSnapshot(
            DateTimeOffset.UtcNow,
            runtimes,
            worlds);
    }

    private RuntimeContextDebugSnapshot CaptureRuntime(RuntimeContext runtime, int index)
    {
        var modules = new List<ModuleDebugSnapshot>(runtime.Modules.Count);
        var procedureModules = new List<ProcedureModuleDebugSnapshot>();
        foreach (var module in runtime.Modules)
        {
            modules.Add(new ModuleDebugSnapshot(
                DebugSnapshotTypeNames.Create(module.GetType()),
                module.Priority,
                module is IUpdateModule));
            if (module is ProcedureModule procedureModule)
            {
                procedureModules.Add(CaptureProcedureModule(procedureModule));
            }
        }

        return new RuntimeContextDebugSnapshot(
            index,
            runtime.IsDisposed,
            modules,
            procedureModules);
    }

    private ProcedureModuleDebugSnapshot CaptureProcedureModule(ProcedureModule module)
    {
        module.TryGetCurrentProcedure(out var currentProcedure);

        var procedures = new List<ProcedureDebugSnapshot>();
        foreach (var procedure in module.Procedures)
        {
            procedures.Add(new ProcedureDebugSnapshot(
                DebugSnapshotTypeNames.Create(procedure.GetType()),
                ReferenceEquals(procedure, currentProcedure)));
        }

        return new ProcedureModuleDebugSnapshot(
            DebugSnapshotTypeNames.Create(module.GetType()),
            module.IsProcedureInitialized,
            currentProcedure?.GetType().Name,
            module.IsProcedureInitialized ? module.CurrentProcedureTime : 0,
            procedures);
    }

    private WorldDebugSnapshot CaptureWorld(World world, GameDebugSnapshotCaptureOptions options)
    {
        var scenes = new List<SceneDebugSnapshot>();
        foreach (var scene in world.Scenes)
        {
            if (MatchesScene(scene, options))
            {
                scenes.Add(CaptureScene(scene, options));
            }
        }

        return new WorldDebugSnapshot(
            world.Name,
            scenes.Count,
            scenes);
    }

    private SceneDebugSnapshot CaptureScene(Scene scene, GameDebugSnapshotCaptureOptions options)
    {
        var valueOptions = new GameDebugComponentValueSerializationOptions
        {
            IncludePayload = options.IncludeComponentPayloads,
            IncludeStructured = options.IncludeStructuredComponentValues,
        };

        var systems = new List<SystemDebugSnapshot>();
        if (options.IncludeSceneSystems)
        {
            foreach (var system in scene.Systems.Systems)
            {
                systems.Add(CaptureSystem(system));
            }
        }

        var storeViews = scene.ComponentStores;
        var componentStores = new List<ComponentStoreDebugSnapshot>(storeViews.Count);
        if (options.IncludeComponentStoreSummaries)
        {
            foreach (var store in storeViews)
            {
                componentStores.Add(CaptureComponentStore(store));
            }
        }

        var entities = new List<EntityDebugSnapshot>();
        var componentTypeIndex = !valueOptions.IncludePayload && !valueOptions.IncludeStructured
            ? BuildEntityComponentTypeIndex(storeViews)
            : null;
        if (options.EntityId is { } entityId)
        {
            if (scene.TryGetEntity(entityId, out var entity))
            {
                entities.Add(CaptureEntity(scene, entity, valueOptions, options, componentTypeIndex));
            }

            return new SceneDebugSnapshot(
                scene.Name,
                scene.EntityCount,
                systems,
                componentStores,
                entities);
        }

        var skipped = 0;
        foreach (var entity in scene.Entities)
        {
            if (skipped < options.EntityOffset)
            {
                skipped++;
                continue;
            }

            if (options.EntityLimit is { } limit && entities.Count >= limit)
            {
                break;
            }

            entities.Add(CaptureEntity(scene, entity, valueOptions, options, componentTypeIndex));
        }

        return new SceneDebugSnapshot(
            scene.Name,
            scene.EntityCount,
            systems,
            componentStores,
            entities);
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
        GameDebugComponentValueSerializationOptions valueOptions,
        GameDebugSnapshotCaptureOptions? options,
        IReadOnlyDictionary<int, IReadOnlyList<Type>>? componentTypeIndex = null)
    {
        if (!valueOptions.IncludePayload && !valueOptions.IncludeStructured)
        {
            return CaptureEntitySummary(scene, entity, options, componentTypeIndex);
        }

        var components = scene.GetComponentsDebugView(
            entity,
            componentType => MatchesComponent(componentType, options));
        var componentTypes = components.ComponentTypes.ToHashSet();
        var visibleTypes = new List<Type>(components.VisibleComponents.Count);
        foreach (var component in components.VisibleComponents)
        {
            visibleTypes.Add(component.ComponentType);
        }

        var summaries = options?.IncludeEntitySummaries == false
            ? GameDebugEntitySummaries.Instance
            : CaptureEntitySummaries(scene, entity, visibleTypes);
        var componentSnapshots = new List<ComponentDebugSnapshot>(components.VisibleComponents.Count);
        foreach (var component in components.VisibleComponents)
        {
            componentSnapshots.Add(new ComponentDebugSnapshot(
                DebugSnapshotTypeNames.Create(component.ComponentType),
                CaptureComponentValue(component, valueOptions),
                CaptureComponentGraph(component.ComponentType, componentTypes, options?.IncludeComponentGraphs ?? true)));
        }

        return new EntityDebugSnapshot(
            entity.Id.Value,
            entity.Version,
            componentSnapshots,
            summaries.Skills,
            summaries.Buffs);
    }

    private static EntityDebugSnapshot CaptureEntitySummary(
        Scene scene,
        Entity entity,
        GameDebugSnapshotCaptureOptions? options,
        IReadOnlyDictionary<int, IReadOnlyList<Type>>? componentTypeIndex = null)
    {
        var componentTypes = componentTypeIndex is not null &&
            componentTypeIndex.TryGetValue(entity.Id.Value, out var indexedTypes)
                ? indexedTypes
                : scene.GetComponentTypes(entity);
        var componentTypeSet = new HashSet<Type>();
        var visibleTypes = new List<Type>();
        foreach (var componentType in componentTypes)
        {
            componentTypeSet.Add(componentType);
            if (MatchesComponent(componentType, options))
            {
                visibleTypes.Add(componentType);
            }
        }

        var summaries = options?.IncludeEntitySummaries == false
            ? GameDebugEntitySummaries.Instance
            : CaptureEntitySummaries(scene, entity, visibleTypes);
        var componentSnapshots = new List<ComponentDebugSnapshot>(visibleTypes.Count);
        foreach (var componentType in visibleTypes)
        {
            componentSnapshots.Add(new ComponentDebugSnapshot(
                DebugSnapshotTypeNames.Create(componentType),
                EmptyComponentValue,
                CaptureComponentGraph(componentType, componentTypeSet, options?.IncludeComponentGraphs ?? true)));
        }

        return new EntityDebugSnapshot(
            entity.Id.Value,
            entity.Version,
            componentSnapshots,
            summaries.Skills,
            summaries.Buffs);
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<Type>> BuildEntityComponentTypeIndex(
        IReadOnlyList<EcsComponentStoreDebugView> stores)
    {
        var index = new Dictionary<int, List<Type>>();
        foreach (var store in stores)
        {
            foreach (var entityId in store.EntityIds)
            {
                if (!index.TryGetValue(entityId, out var componentTypes))
                {
                    componentTypes = [];
                    index.Add(entityId, componentTypes);
                }

                componentTypes.Add(store.ComponentType);
            }
        }

        return index.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<Type>)pair.Value);
    }

    private ComponentValueDebugSnapshot CaptureComponentValue(
        EcsComponentDebugView component,
        GameDebugComponentValueSerializationOptions valueOptions)
    {
        if (!valueOptions.IncludePayload && !valueOptions.IncludeStructured)
        {
            return EmptyComponentValue;
        }

        return _componentValueSerializer.Serialize(component.Value, valueOptions);
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

    private static bool MatchesComponent(Type componentType, GameDebugSnapshotCaptureOptions? options)
    {
        if (options is null || string.IsNullOrWhiteSpace(options.ComponentTypeFullName))
        {
            return true;
        }

        var typeInfo = DebugSnapshotTypeNames.Create(componentType);
        return StringComparer.Ordinal.Equals(typeInfo.FullName, options.ComponentTypeFullName)
            && (string.IsNullOrWhiteSpace(options.ComponentAssemblyName)
                || StringComparer.Ordinal.Equals(typeInfo.AssemblyName, options.ComponentAssemblyName));
    }

    private static ComponentGraphDebugSnapshot CaptureComponentGraph(
        Type componentType,
        ISet<Type> componentTypes,
        bool includeGraphMetadata)
    {
        if (!includeGraphMetadata)
        {
            var typeInfo = DebugSnapshotTypeNames.Create(componentType);
            return new ComponentGraphDebugSnapshot(
                $"{typeInfo.AssemblyName}:{typeInfo.FullName}",
                ParentId: null,
                ParentType: null,
                Group: null,
                Order: 0);
        }

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

    private static GameDebugEntitySummaries CaptureEntitySummaries(
        Scene scene,
        Entity entity,
        IReadOnlyCollection<Type> visibleTypes)
    {
        var builder = new GameDebugEntitySummaryBuilder();
        foreach (var provider in EntitySummaryProviders)
        {
            provider.Capture(scene, entity, visibleTypes, builder);
        }

        return builder.Build();
    }

    private static IReadOnlyList<SkillDebugSnapshot> CaptureSkillBook(SkillBookComponent skillBook)
    {
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

    private static IReadOnlyList<BuffDebugSnapshot> CaptureBuffCollection(BuffCollectionComponent buffCollection)
    {
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

    private static IReadOnlyList<string> FormatTags(GameplayTagContainer tags)
    {
        return tags.Tags
            .Select(static tag => tag.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private interface IGameDebugEntitySummaryProvider
    {
        void Capture(
            Scene scene,
            Entity entity,
            IReadOnlyCollection<Type> visibleTypes,
            GameDebugEntitySummaryBuilder builder);
    }

    private sealed class SkillDebugEntitySummaryProvider : IGameDebugEntitySummaryProvider
    {
        public void Capture(
            Scene scene,
            Entity entity,
            IReadOnlyCollection<Type> visibleTypes,
            GameDebugEntitySummaryBuilder builder)
        {
            if (!visibleTypes.Contains(typeof(SkillBookComponent)) ||
                !scene.TryGetComponent(entity, typeof(SkillBookComponent), out var component) ||
                component is not SkillBookComponent skillBook)
            {
                return;
            }

            builder.SetSkills(CaptureSkillBook(skillBook));
        }
    }

    private sealed class BuffDebugEntitySummaryProvider : IGameDebugEntitySummaryProvider
    {
        public void Capture(
            Scene scene,
            Entity entity,
            IReadOnlyCollection<Type> visibleTypes,
            GameDebugEntitySummaryBuilder builder)
        {
            if (!visibleTypes.Contains(typeof(BuffCollectionComponent)) ||
                !scene.TryGetComponent(entity, typeof(BuffCollectionComponent), out var component) ||
                component is not BuffCollectionComponent buffCollection)
            {
                return;
            }

            builder.SetBuffs(CaptureBuffCollection(buffCollection));
        }
    }

    private sealed class GameDebugEntitySummaryBuilder
    {
        private IReadOnlyList<SkillDebugSnapshot> _skills = Array.Empty<SkillDebugSnapshot>();
        private IReadOnlyList<BuffDebugSnapshot> _buffs = Array.Empty<BuffDebugSnapshot>();

        public void SetSkills(IReadOnlyList<SkillDebugSnapshot> skills)
        {
            _skills = skills;
        }

        public void SetBuffs(IReadOnlyList<BuffDebugSnapshot> buffs)
        {
            _buffs = buffs;
        }

        public GameDebugEntitySummaries Build()
        {
            return new GameDebugEntitySummaries(_skills, _buffs);
        }
    }

    private sealed record GameDebugEntitySummaries(
        IReadOnlyList<SkillDebugSnapshot> Skills,
        IReadOnlyList<BuffDebugSnapshot> Buffs)
    {
        public static GameDebugEntitySummaries Instance { get; } = new(
            Array.Empty<SkillDebugSnapshot>(),
            Array.Empty<BuffDebugSnapshot>());
    }
}
