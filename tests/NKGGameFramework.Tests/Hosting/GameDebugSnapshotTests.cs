using NKGGameFramework.Core;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;
using NKGGameFramework.Hosting.Diagnostics;
using OdinSerializer;

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
        Assert.Equal("odin-json", buffCollectionComponent.Value.Format);
        Assert.Null(buffCollectionComponent.Value.Error);
        Assert.NotNull(buffCollectionComponent.Value.Payload);

        var buffCollectionValue = buffCollectionComponent.Value.Structured;
        Assert.NotNull(buffCollectionValue);
        Assert.Equal("object", buffCollectionValue!.Kind);
        var buffCollectionEntries = FindChild(buffCollectionValue, "_buffs");
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
    public void Debug_odin_serializer_handles_null_values_without_error()
    {
        var valueSerializer = new OdinGameDebugComponentValueSerializer();

        var value = valueSerializer.Serialize(null!);

        Assert.Equal("odin-json", value.Format);
        Assert.Null(value.Payload);
        Assert.Null(value.Error);
        Assert.NotNull(value.Structured);
        Assert.Equal("null", value.Structured!.Kind);
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
    public void Capture_summary_component_list_does_not_serialize_component_values()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var entity = scene.CreateEntity()
            .Add(new PositionComponent(1, 2))
            .Add(new VelocityComponent(3, 4));
        var session = new GameDebugSession().Register(world);
        var snapshots = new GameDebugSnapshotProvider(
            session,
            new ThrowingComponentValueSerializer());

        var snapshot = snapshots.Capture(new GameDebugSnapshotCaptureOptions
        {
            IncludeComponentPayloads = false,
            IncludeStructuredComponentValues = false,
        });

        var sceneSnapshot = Assert.Single(Assert.Single(snapshot.Worlds).Scenes);
        var entitySnapshot = Assert.Single(sceneSnapshot.Entities, candidate => candidate.Id == entity.Id.Value);
        Assert.Contains(entitySnapshot.Components, component => component.Type.Name == nameof(PositionComponent));
        Assert.Contains(entitySnapshot.Components, component => component.Type.Name == nameof(VelocityComponent));
        Assert.All(entitySnapshot.Components, component =>
        {
            Assert.Equal("none", component.Value.Format);
            Assert.Null(component.Value.Payload);
            Assert.Null(component.Value.Structured);
            Assert.Null(component.Value.Error);
        });
    }

    [Fact]
    public void Capture_options_filter_component_values_to_a_single_component_type()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var entity = scene.CreateEntity()
            .Add(new PositionComponent(1, 2))
            .Add(new VelocityComponent(3, 4));
        var componentType = typeof(PositionComponent);
        var session = new GameDebugSession().Register(world);
        var snapshots = new GameDebugSnapshotProvider(
            session,
            new OdinGameDebugComponentValueSerializer());

        var snapshot = snapshots.Capture(new GameDebugSnapshotCaptureOptions
        {
            WorldName = world.Name,
            SceneName = scene.Name,
            EntityId = entity.Id.Value,
            ComponentTypeFullName = componentType.FullName,
            ComponentAssemblyName = componentType.Assembly.GetName().Name,
            IncludeComponentPayloads = true,
            IncludeStructuredComponentValues = true,
        });

        var sceneSnapshot = Assert.Single(Assert.Single(snapshot.Worlds).Scenes);
        var entitySnapshot = Assert.Single(sceneSnapshot.Entities);
        var component = Assert.Single(entitySnapshot.Components);
        Assert.Equal(nameof(PositionComponent), component.Type.Name);
        Assert.NotNull(component.Value.Payload);
        Assert.NotNull(component.Value.Structured);
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
    public void Structured_component_values_stop_at_runtime_reference_boundaries()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var entity = scene.CreateEntity();
        var valueSerializer = new OdinGameDebugComponentValueSerializer();

        var value = valueSerializer.Serialize(new RuntimeReferenceComponent
        {
            Scene = scene,
            Owner = entity,
            Values = [1, 2, 3],
        });

        Assert.NotNull(value.Structured);
        var sceneNode = FindChild(value.Structured!, nameof(RuntimeReferenceComponent.Scene));
        var ownerNode = FindChild(value.Structured!, nameof(RuntimeReferenceComponent.Owner));
        var valuesNode = FindChild(value.Structured!, nameof(RuntimeReferenceComponent.Values));
        Assert.Equal("reference", sceneNode.Kind);
        Assert.Equal("battle", sceneNode.Value);
        Assert.Equal("Runtime reference boundary.", sceneNode.Error);
        Assert.DoesNotContain(sceneNode.Children, child => child.Name == nameof(Scene.ComponentStores));
        Assert.Equal("object", ownerNode.Kind);
        Assert.Equal("list", valuesNode.Kind);
        Assert.Equal(3, valuesNode.Children.Count);
    }

    [Fact]
    public void Structured_component_values_capture_hashset_as_list_items()
    {
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var value = valueSerializer.Serialize(new CollectionComponent
        {
            Tags = ["beta", "alpha"],
        });

        Assert.NotNull(value.Structured);
        var tags = FindChild(value.Structured!, nameof(CollectionComponent.Tags));

        Assert.Equal("list", tags.Kind);
        Assert.Equal(typeof(string).FullName, tags.ElementType?.FullName);
        Assert.Equal(["alpha", "beta"], tags.Children.Select(child => child.Value).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(tags.Children, child => child.Name == "Count");
        Assert.DoesNotContain(tags.Children, child => child.Name == "Comparer");

        var edited = SetChild(value.Structured!, nameof(CollectionComponent.Tags), child => child with
        {
            Children =
            [
                child.Children[0] with { Name = "[0]", Value = "gamma" },
                child.Children[1] with { Name = "[1]", Value = "delta" },
            ],
        });
        var updated = (CollectionComponent)valueSerializer.Deserialize(
            value with { Structured = edited },
            typeof(CollectionComponent));

        Assert.Equal(["delta", "gamma"], updated.Tags.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Debug_odin_policy_serializes_public_component_surface_without_private_cache_fields()
    {
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var value = valueSerializer.Serialize(new PolicyComponent(
            publicField: 1,
            publicProperty: 2,
            readOnlyProperty: 3,
            privateCache: 4));

        Assert.NotNull(value.Payload);
        Assert.DoesNotContain("_privateCache", value.Payload, StringComparison.Ordinal);
        Assert.NotNull(value.Structured);
        Assert.Contains(value.Structured.Children, child => child.Name == nameof(PolicyComponent.PublicField));
        Assert.Contains(value.Structured.Children, child => child.Name == nameof(PolicyComponent.PublicProperty));
        Assert.Contains(value.Structured.Children, child => child.Name == nameof(PolicyComponent.ReadOnlyProperty));
        Assert.DoesNotContain(value.Structured.Children, child => child.Name == nameof(PolicyComponent.PrivateCache));
        Assert.DoesNotContain(value.Structured.Children, child => child.Name == "_privateCache");

        var restored = (PolicyComponent)valueSerializer.Deserialize(
            value with { Structured = null },
            typeof(PolicyComponent));

        Assert.Equal(1, restored.PublicField);
        Assert.Equal(2, restored.PublicProperty);
        Assert.Equal(3, restored.ReadOnlyProperty);
        Assert.Equal(0, restored.PrivateCache);
    }

    [Fact]
    public void Debug_odin_policy_excludes_nonserialized_members()
    {
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var value = valueSerializer.Serialize(new NonSerializedPolicyComponent(
            visible: 1,
            odinButExcluded: 2,
            propertyExcluded: 3));

        Assert.NotNull(value.Payload);
        Assert.Contains(nameof(NonSerializedPolicyComponent.Visible), value.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("_odinButExcluded", value.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(NonSerializedPolicyComponent.PropertyExcluded), value.Payload, StringComparison.Ordinal);
        Assert.NotNull(value.Structured);
        Assert.Contains(value.Structured.Children, child => child.Name == nameof(NonSerializedPolicyComponent.Visible));
        Assert.DoesNotContain(value.Structured.Children, child => child.Name == "_odinButExcluded");
        Assert.DoesNotContain(value.Structured.Children, child => child.Name == nameof(NonSerializedPolicyComponent.PropertyExcluded));

        var restored = (NonSerializedPolicyComponent)valueSerializer.Deserialize(
            value with { Structured = null },
            typeof(NonSerializedPolicyComponent));

        Assert.Equal(1, restored.Visible);
        Assert.Equal(0, restored.OdinButExcluded);
        Assert.Equal(0, restored.PropertyExcluded);
    }

    [Fact]
    public void Debug_odin_policy_serializes_explicit_odin_property()
    {
        var valueSerializer = new OdinGameDebugComponentValueSerializer();
        var value = valueSerializer.Serialize(new OdinPropertyComponent(7));

        Assert.NotNull(value.Payload);
        Assert.Contains("PrivateValue", value.Payload, StringComparison.Ordinal);
        Assert.NotNull(value.Structured);
        Assert.Contains(value.Structured.Children, child => child.Name == "PrivateValue");

        var restored = (OdinPropertyComponent)valueSerializer.Deserialize(
            value with { Structured = null },
            typeof(OdinPropertyComponent));

        Assert.Equal(7, restored.VisibleValue);
    }


    [Fact]
    public void Debug_odin_policy_serializes_first_party_private_gameplay_state()
    {
        using var world = new World("debug-world");
        var scene = world.CreateScene("battle");
        var entity = scene.CreateEntity()
            .Add(new GameplayTagComponent(GameplayTagContainer.From("Unit.Hero.Mage")));
        SkillManager.Learn(entity, new SkillDefinition
        {
            Id = "fireball",
            DisplayName = "Fireball",
            Cooldowns = { [1] = TimeSpan.FromSeconds(3) },
        }, level: 1);
        var valueSerializer = new OdinGameDebugComponentValueSerializer();

        var tags = valueSerializer.Serialize(entity.Get<GameplayTagComponent>());
        var skills = valueSerializer.Serialize(entity.Get<SkillBookComponent>());

        Assert.Null(tags.Error);
        Assert.NotNull(tags.Payload);
        Assert.Contains("Unit.Hero.Mage", tags.Payload, StringComparison.Ordinal);
        Assert.NotNull(tags.Structured);
        Assert.Contains(tags.Structured.Children, child => child.Name == "_tags");

        Assert.Null(skills.Error);
        Assert.NotNull(skills.Payload);
        Assert.Contains("fireball", skills.Payload, StringComparison.Ordinal);
        Assert.NotNull(skills.Structured);
        Assert.Contains(skills.Structured.Children, child => child.Name == "_skills");
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

    private readonly record struct VelocityComponent(double X, double Y) : IComponent;

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

    private struct RuntimeReferenceComponent : IComponent
    {
        public Scene? Scene;

        public Entity Owner;

        public List<int> Values;
    }

    private struct CollectionComponent : IComponent
    {
        public HashSet<string> Tags;
    }

    private struct PolicyComponent : IComponent
    {
        public int PublicField;
        private int _privateCache;

        public PolicyComponent(
            int publicField,
            int publicProperty,
            int readOnlyProperty,
            int privateCache)
        {
            PublicField = publicField;
            PublicProperty = publicProperty;
            ReadOnlyProperty = readOnlyProperty;
            _privateCache = privateCache;
        }

        public int PublicProperty { get; set; }

        public int ReadOnlyProperty { get; }

        public int PrivateCache => _privateCache;
    }

    private struct NonSerializedPolicyComponent : IComponent
    {
        public int Visible;

        [OdinSerialize]
        [NonSerialized]
        private int _odinButExcluded;

        public NonSerializedPolicyComponent(
            int visible,
            int odinButExcluded,
            int propertyExcluded)
        {
            Visible = visible;
            _odinButExcluded = odinButExcluded;
            PropertyExcluded = propertyExcluded;
        }

        public int OdinButExcluded => _odinButExcluded;

        [field: NonSerialized]
        public int PropertyExcluded { get; set; }
    }

    private struct OdinPropertyComponent : IComponent
    {
        public OdinPropertyComponent(int privateValue)
        {
            PrivateValue = privateValue;
        }

        public int VisibleValue => PrivateValue;

        [OdinSerialize]
        private int PrivateValue { get; set; }
    }

    private sealed class ThrowingComponentValueSerializer : IGameDebugComponentValueSerializer
    {
        public ComponentValueDebugSnapshot Serialize(
            object value,
            GameDebugComponentValueSerializationOptions? options = null)
        {
            throw new InvalidOperationException("Summary snapshots should not serialize component values.");
        }

        public object Deserialize(ComponentValueDebugSnapshot value, Type expectedType)
        {
            throw new InvalidOperationException("Summary snapshots should not deserialize component values.");
        }
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
