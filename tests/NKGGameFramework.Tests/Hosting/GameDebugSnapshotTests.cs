using NKGGameFramework.Core;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;
using NKGGameFramework.Hosting.Diagnostics;

namespace NKGGameFramework.Tests.Hosting;

[Collection(GameDebugRegistryCollection.Name)]
public sealed class GameDebugSnapshotTests
{
    [Fact]
    public void Capture_discovers_runtime_and_world_without_session_registration()
    {
        GameDebugRuntimeRegistry.Clear();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("auto-debug-world");
            var scene = world.CreateScene("battle");
            var entity = scene.CreateEntity()
                .Add(new PositionComponent(12, 34));
            var snapshots = new GameDebugSnapshotProvider(
                new GameDebugSession(),
                new OdinGameDebugComponentValueSerializer());

            var snapshot = snapshots.Capture();

            Assert.Contains(snapshot.Runtimes, runtimeSnapshot => runtimeSnapshot.IsDisposed is false);
            var worldSnapshot = Assert.Single(
                snapshot.Worlds,
                worldSnapshot => worldSnapshot.Name == world.Name);
            var sceneSnapshot = Assert.Single(worldSnapshot.Scenes);
            Assert.Equal(scene.Name, sceneSnapshot.Name);
            Assert.Contains(sceneSnapshot.Entities, entitySnapshot => entitySnapshot.Id == entity.Id.Value);
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
        }
    }

    [Fact]
    public void Capture_includes_runtime_world_components_skills_and_buffs()
    {
        using var runtime = new RuntimeContext();
        var procedures = runtime.RegisterModule(new ProcedureModule());
        procedures.Initialize(new BootProcedure(), new GameplayProcedure());
        procedures.StartProcedure<BootProcedure>();

        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        scene.Systems.Add(new SkillCooldownSystem());

        var caster = scene.CreateEntity()
            .Add(new PositionComponent(12, 34));
        var target = scene.CreateEntity();

        SkillManager.Learn(caster, new SkillDefinition
        {
            Id = "fireball",
            DisplayName = "Fireball",
            Kind = SkillKind.Active,
            CostKind = SkillCostKind.Mana,
            Cooldowns = { [1] = TimeSpan.FromSeconds(3) },
            Costs = { [1] = 25 },
            Tags = GameplayTagContainer.From("Skill.Fire"),
            ResourceLocations = { "fx/fireball" },
            Effects =
            {
                new SkillEffectDefinition
                {
                    Key = "damage",
                },
            },
        }, level: 1);

        BuffManager.Apply(caster, target, new BuffDefinition
        {
            Id = "burn",
            DisplayName = "Burn",
            EffectKey = "dot",
            Duration = TimeSpan.FromSeconds(5),
            Tags = GameplayTagContainer.From("State.Burning"),
            MaxStacks = 3,
        }, level: 2, stacks: 2);

        var session = new GameDebugSession()
            .Register(runtime)
            .Register(world);
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var snapshots = new GameDebugSnapshotProvider(
            session,
            valueSerializer);

        var snapshot = snapshots.Capture();

        var runtimeSnapshot = Assert.Single(snapshot.Runtimes);
        var procedureModule = Assert.Single(runtimeSnapshot.ProcedureModules);
        Assert.Equal(nameof(BootProcedure), procedureModule.CurrentProcedure);
        Assert.Contains(procedureModule.Procedures, procedure => procedure.Type.Name == nameof(GameplayProcedure));

        var worldSnapshot = Assert.Single(snapshot.Worlds);
        Assert.Equal("debug-world", worldSnapshot.Name);

        var sceneSnapshot = Assert.Single(worldSnapshot.Scenes);
        Assert.Equal("battle", sceneSnapshot.Name);
        Assert.Equal(2, sceneSnapshot.EntityCount);
        Assert.Contains(sceneSnapshot.Systems, system => system.Type.Name == nameof(SkillCooldownSystem));
        Assert.Contains(sceneSnapshot.ComponentStores, store => store.Type.Name == nameof(PositionComponent));

        var casterSnapshot = Assert.Single(sceneSnapshot.Entities, entity => entity.Id == caster.Id.Value);
        var positionComponent = Assert.Single(
            casterSnapshot.Components,
            component => component.Type.Name == nameof(PositionComponent));
        Assert.Equal("odin-json", positionComponent.Value.Format);
        Assert.False(string.IsNullOrWhiteSpace(positionComponent.Value.Payload));
        Assert.Null(positionComponent.Value.Error);
        Assert.NotNull(positionComponent.Value.Structured);

        var skill = Assert.Single(casterSnapshot.Skills);
        Assert.Equal("fireball", skill.Id);
        Assert.Equal("Fireball", skill.DisplayName);
        Assert.Equal(25, skill.Cost);
        Assert.Equal(3, skill.CooldownSeconds);
        Assert.Contains("Skill.Fire", skill.Tags);
        Assert.Contains("fx/fireball", skill.ResourceLocations);
        Assert.Contains("damage", skill.EffectKeys);

        var targetSnapshot = Assert.Single(sceneSnapshot.Entities, entity => entity.Id == target.Id.Value);
        var buffCollectionComponent = Assert.Single(
            targetSnapshot.Components,
            component => component.Type.Name == nameof(BuffCollectionComponent));
        Assert.Equal("debug-summary", buffCollectionComponent.Value.Format);
        Assert.Null(buffCollectionComponent.Value.Error);
        Assert.NotNull(buffCollectionComponent.Value.Payload);
        Assert.True(buffCollectionComponent.Value.Payload!.Length < 2048);

        var buffCollectionValue = buffCollectionComponent.Value.Structured;
        Assert.NotNull(buffCollectionValue);
        Assert.Equal("object", buffCollectionValue!.Kind);
        Assert.False(buffCollectionValue.Editable);
        Assert.Equal("1", FindChild(buffCollectionValue, "Count").Value);
        Assert.Equal("1", FindChild(buffCollectionValue, "ActiveCount").Value);
        var buffCollectionEntries = FindChild(buffCollectionValue, "Buffs");
        Assert.Equal("list", buffCollectionEntries.Kind);
        Assert.Single(buffCollectionEntries.Children);

        var buff = Assert.Single(targetSnapshot.Buffs);
        Assert.Equal("burn", buff.Id);
        Assert.Equal("Burn", buff.DisplayName);
        Assert.Equal(2, buff.Level);
        Assert.Equal(2, buff.Stacks);
        Assert.Equal(5, buff.RemainingDurationSeconds);
        Assert.Contains("State.Burning", buff.Tags);
    }

    [Fact]
    public void Capture_options_filter_page_and_trim_component_values()
    {
        using var world = new World("debug-world");
        var battle = world.CreateScene("battle");
        var menu = world.CreateScene("menu");
        battle.CreateEntity()
            .Add(new PositionComponent(1, 1));
        var second = battle.CreateEntity()
            .Add(new PositionComponent(2, 2));
        battle.CreateEntity()
            .Add(new PositionComponent(3, 3));
        menu.CreateEntity()
            .Add(new PositionComponent(4, 4));
        var session = new GameDebugSession().Register(world);
        var snapshots = new GameDebugSnapshotProvider(
            session,
            new OdinGameDebugComponentValueSerializer());

        var snapshot = snapshots.Capture(new GameDebugSnapshotCaptureOptions
        {
            WorldName = world.Name,
            SceneName = battle.Name,
            EntityOffset = 1,
            EntityLimit = 1,
            IncludeComponentPayloads = false,
            IncludeStructuredComponentValues = false,
        });

        var worldSnapshot = Assert.Single(snapshot.Worlds);
        var sceneSnapshot = Assert.Single(worldSnapshot.Scenes);
        var entitySnapshot = Assert.Single(sceneSnapshot.Entities);
        var component = Assert.Single(entitySnapshot.Components);
        Assert.Equal("battle", sceneSnapshot.Name);
        Assert.Equal(3, sceneSnapshot.EntityCount);
        Assert.Equal(second.Id.Value, entitySnapshot.Id);
        Assert.Null(component.Value.Payload);
        Assert.Null(component.Value.Structured);
        Assert.Null(component.Value.Error);
    }

    [Fact]
    public void Mutations_write_any_component_value_through_odin_payload()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var entity = scene.CreateEntity()
            .Add(new PositionComponent(12, 34));

        var session = new GameDebugSession().Register(world);
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var mutations = new GameDebugMutationHandler(
            session,
            new GameDebugOptions { EnableMutations = true },
            valueSerializer);
        var componentType = typeof(PositionComponent);

        var result = mutations.Execute(new GameDebugMutationRequest(
            world.Name,
            scene.Name,
            entity.Id.Value,
            entity.Version,
            componentType.FullName!,
            componentType.Assembly.GetName().Name!,
            valueSerializer.Serialize(new PositionComponent(99, 34))));

        Assert.True(result.Succeeded);
        Assert.Equal(99, entity.Get<PositionComponent>().X);
        Assert.Equal(34, entity.Get<PositionComponent>().Y);
    }

    [Fact]
    public void Mutations_are_disabled_by_default()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var entity = scene.CreateEntity()
            .Add(new PositionComponent(12, 34));

        var session = new GameDebugSession().Register(world);
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var mutations = new GameDebugMutationHandler(
            session,
            new GameDebugOptions(),
            valueSerializer);
        var componentType = typeof(PositionComponent);

        var result = mutations.Execute(new GameDebugMutationRequest(
            world.Name,
            scene.Name,
            entity.Id.Value,
            entity.Version,
            componentType.FullName!,
            componentType.Assembly.GetName().Name!,
            valueSerializer.Serialize(new PositionComponent(99, 34))));

        Assert.False(result.Succeeded);
        Assert.Equal("Debug mutations are disabled.", result.Message);
        Assert.Equal(12, entity.Get<PositionComponent>().X);
    }

    [Fact]
    public void Capture_includes_component_graph_metadata()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var linkedEntity = scene.CreateEntity()
            .Add(new GraphRootComponent(1))
            .Add(new GraphChildComponent(2));
        var orphanEntity = scene.CreateEntity()
            .Add(new GraphOrphanComponent(3));

        var session = new GameDebugSession().Register(world);
        var snapshots = new GameDebugSnapshotProvider(
            session,
            new OdinGameDebugComponentValueSerializer());

        var snapshot = snapshots.Capture();

        var sceneSnapshot = Assert.Single(Assert.Single(snapshot.Worlds).Scenes);
        var linkedEntitySnapshot = Assert.Single(sceneSnapshot.Entities, entity => entity.Id == linkedEntity.Id.Value);
        var root = Assert.Single(linkedEntitySnapshot.Components, component => component.Type.Name == nameof(GraphRootComponent));
        var child = Assert.Single(linkedEntitySnapshot.Components, component => component.Type.Name == nameof(GraphChildComponent));
        Assert.Equal(CreateGraphId(typeof(GraphRootComponent)), root.Graph.Id);
        Assert.Null(root.Graph.ParentId);
        Assert.Null(root.Graph.ParentType);
        Assert.Equal("Debug/Test", root.Graph.Group);
        Assert.Equal(10, root.Graph.Order);
        Assert.Equal(root.Graph.Id, child.Graph.ParentId);
        Assert.NotNull(child.Graph.ParentType);
        Assert.Equal(nameof(GraphRootComponent), child.Graph.ParentType!.Name);
        Assert.Equal("Debug/Test", child.Graph.Group);
        Assert.Equal(20, child.Graph.Order);

        var orphanEntitySnapshot = Assert.Single(sceneSnapshot.Entities, entity => entity.Id == orphanEntity.Id.Value);
        var orphan = Assert.Single(orphanEntitySnapshot.Components, component => component.Type.Name == nameof(GraphOrphanComponent));
        Assert.Null(orphan.Graph.ParentId);
        Assert.NotNull(orphan.Graph.ParentType);
        Assert.Equal(nameof(GraphRootComponent), orphan.Graph.ParentType!.Name);
    }

    [Fact]
    public void Capture_includes_structured_component_value_fields()
    {
        var valueSerializer = new OdinGameDebugComponentValueSerializer();

        var value = valueSerializer.Serialize(new EditableComponent
        {
            Enabled = true,
            Count = 3,
            Name = "caster",
            Values = [1, 2],
            Stats = new EditableStats
            {
                Multiplier = 1.5,
            },
        });

        Assert.NotNull(value.Structured);
        var root = value.Structured;
        Assert.Equal("object", root.Kind);
        Assert.Equal("boolean", FindChild(root, nameof(EditableComponent.Enabled)).Kind);
        Assert.Equal("integer", FindChild(root, nameof(EditableComponent.Count)).Kind);
        Assert.Equal("string", FindChild(root, nameof(EditableComponent.Name)).Kind);

        var values = FindChild(root, nameof(EditableComponent.Values));
        Assert.Equal("list", values.Kind);
        Assert.Equal(2, values.Children.Count);
        Assert.Equal("integer", values.Children[0].Kind);

        var stats = FindChild(root, nameof(EditableComponent.Stats));
        Assert.Equal("object", stats.Kind);
        Assert.Equal("number", FindChild(stats, nameof(EditableStats.Multiplier)).Kind);
    }

    [Fact]
    public void Mutations_write_structured_component_value_fields()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var entity = scene.CreateEntity()
            .Add(new EditableComponent
            {
                Enabled = true,
                Count = 3,
                Name = "caster",
                Values = [1, 2],
                Stats = new EditableStats
                {
                    Multiplier = 1.5,
                },
            });

        var session = new GameDebugSession().Register(world);
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var mutations = new GameDebugMutationHandler(
            session,
            new GameDebugOptions { EnableMutations = true },
            valueSerializer);
        var componentType = typeof(EditableComponent);
        var value = valueSerializer.Serialize(entity.Get<EditableComponent>());
        var edited = SetChild(
            SetChild(
                SetChild(
                    SetChild(
                        SetChild(value.Structured!, nameof(EditableComponent.Enabled), child => child with { Value = "false" }),
                        nameof(EditableComponent.Count),
                        child => child with { Value = "8" }),
                    nameof(EditableComponent.Name),
                    child => child with { Value = "enemy" }),
                nameof(EditableComponent.Values),
                child => child with
                {
                    Children =
                    [
                        child.Children[0] with { Name = "[0]", Value = "9" },
                        child.Children[1] with { Name = "[1]", Value = "10" },
                        child.ElementTemplate! with { Name = "[2]", Value = "11" },
                    ],
                }),
            nameof(EditableComponent.Stats),
            child => SetChild(child, nameof(EditableStats.Multiplier), stat => stat with { Value = "2.25" }));

        var result = mutations.Execute(new GameDebugMutationRequest(
            world.Name,
            scene.Name,
            entity.Id.Value,
            entity.Version,
            componentType.FullName!,
            componentType.Assembly.GetName().Name!,
            value with { Structured = edited }));

        var component = entity.Get<EditableComponent>();
        Assert.True(result.Succeeded);
        Assert.False(component.Enabled);
        Assert.Equal(8, component.Count);
        Assert.Equal("enemy", component.Name);
        Assert.Equal([9, 10, 11], component.Values);
        Assert.Equal(2.25, component.Stats.Multiplier);
    }

    [Fact]
    public void Structured_mutations_preserve_readonly_public_properties_from_payload()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var entity = scene.CreateEntity()
            .Add(new MixedComponent(0.5)
            {
                Count = 3,
            });

        var session = new GameDebugSession().Register(world);
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var mutations = new GameDebugMutationHandler(
            session,
            new GameDebugOptions { EnableMutations = true },
            valueSerializer);
        var componentType = typeof(MixedComponent);
        var value = valueSerializer.Serialize(entity.Get<MixedComponent>());
        var edited = SetChild(value.Structured!, nameof(MixedComponent.Count), child => child with { Value = "8" });

        var result = mutations.Execute(new GameDebugMutationRequest(
            world.Name,
            scene.Name,
            entity.Id.Value,
            entity.Version,
            componentType.FullName!,
            componentType.Assembly.GetName().Name!,
            value with { Structured = edited }));

        var component = entity.Get<MixedComponent>();
        Assert.True(result.Succeeded);
        Assert.Equal(8, component.Count);
        Assert.Equal(0.5, component.Multiplier);
    }

    private sealed class BootProcedure : ProcedureBase
    {
    }

    private sealed class GameplayProcedure : ProcedureBase
    {
    }

    private readonly record struct PositionComponent(double X, double Y) : IComponent;

    [ComponentGraph(Group = "Debug/Test", Order = 10)]
    private readonly record struct GraphRootComponent(int Value) : IComponent;

    [ComponentGraph(Parent = typeof(GraphRootComponent), Group = "Debug/Test", Order = 20)]
    private readonly record struct GraphChildComponent(int Value) : IComponent;

    [ComponentGraph(Parent = typeof(GraphRootComponent), Group = "Debug/Test", Order = 30)]
    private readonly record struct GraphOrphanComponent(int Value) : IComponent;

    private struct EditableComponent : IComponent
    {
        public bool Enabled;

        public int Count;

        public string Name;

        public List<int> Values;

        public EditableStats Stats;
    }

    private struct EditableStats
    {
        public double Multiplier;
    }

    private struct MixedComponent(double multiplier) : IComponent
    {
        public int Count;

        public double Multiplier { get; } = multiplier;
    }

    private static ComponentValueDebugNode FindChild(ComponentValueDebugNode node, string name)
    {
        return Assert.Single(node.Children, child => child.Name == name);
    }

    private static ComponentValueDebugNode SetChild(
        ComponentValueDebugNode node,
        string name,
        Func<ComponentValueDebugNode, ComponentValueDebugNode> update)
    {
        return node with
        {
            Children = node.Children
                .Select(child => child.Name == name ? update(child) : child)
            .ToArray(),
        };
    }

    private static string CreateGraphId(Type componentType)
    {
        return $"{componentType.Assembly.GetName().Name}:{componentType.FullName}";
    }
}
