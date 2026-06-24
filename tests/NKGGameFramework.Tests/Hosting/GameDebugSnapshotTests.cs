using Microsoft.Extensions.Options;
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

        var skill = Assert.Single(casterSnapshot.Skills);
        Assert.Equal("fireball", skill.Id);
        Assert.Equal("Fireball", skill.DisplayName);
        Assert.Equal(25, skill.Cost);
        Assert.Equal(3, skill.CooldownSeconds);
        Assert.Contains("Skill.Fire", skill.Tags);
        Assert.Contains("fx/fireball", skill.ResourceLocations);
        Assert.Contains("damage", skill.EffectKeys);

        var targetSnapshot = Assert.Single(sceneSnapshot.Entities, entity => entity.Id == target.Id.Value);
        var buff = Assert.Single(targetSnapshot.Buffs);
        Assert.Equal("burn", buff.Id);
        Assert.Equal("Burn", buff.DisplayName);
        Assert.Equal(2, buff.Level);
        Assert.Equal(2, buff.Stacks);
        Assert.Equal(5, buff.RemainingDurationSeconds);
        Assert.Contains("State.Burning", buff.Tags);
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
            Options.Create(new GameDebugOptions()),
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

    private sealed class BootProcedure : ProcedureBase
    {
    }

    private sealed class GameplayProcedure : ProcedureBase
    {
    }

    private readonly record struct PositionComponent(double X, double Y) : IComponent;
}
