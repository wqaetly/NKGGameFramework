using NKGGameFramework.Ecs;
using NKGGameFramework.Serialization;

namespace NKGGameFramework.Ecs.Tests;

public sealed class EcsCompositionTests
{
    [Fact]
    public void SystemsOperateOnMatchingComponentComposition()
    {
        var scene = new Scene("combat");
        scene.Systems.Add(new MovementSystem());

        var movingEntity = scene.CreateEntity()
            .Add(new Position(0, 0))
            .Add(new Velocity(2, 3));
        var staticEntity = scene.CreateEntity()
            .Add(new Position(10, 10));
        var velocityOnlyEntity = scene.CreateEntity()
            .Add(new Velocity(100, 100));

        scene.Update(0.5, 0.5);

        Assert.Equal(1, movingEntity.Get<Position>().X);
        Assert.Equal(1.5, movingEntity.Get<Position>().Y);
        Assert.Equal(10, staticEntity.Get<Position>().X);
        Assert.False(velocityOnlyEntity.Has<Position>());
    }

    [Fact]
    public void SystemOrderIsDeterministic()
    {
        var scene = new Scene("ordered");
        var calls = new List<string>();
        scene.Systems.Add(new RecordingSystem("last", order: 20, calls));
        scene.Systems.Add(new RecordingSystem("first", order: 10, calls));

        scene.Update(0.016, 0.016);

        Assert.Equal(["first", "last"], calls);
    }

    [Fact]
    public void SystemsReceiveLifecycleCallbacks()
    {
        var scene = new Scene("lifecycle");
        var calls = new List<string>();
        var system = new LifecycleSystem(calls);

        scene.Systems.Add(system);
        scene.Update(0.016, 0.016);

        system.Enabled = false;
        scene.Update(0.016, 0.016);

        system.Enabled = true;
        scene.Update(0.016, 0.016);
        scene.Dispose();

        Assert.Equal(["create", "start", "update", "stop", "start", "update", "stop", "destroy"], calls);
    }

    [Fact]
    public void SystemsReactToComponentStructuralChanges()
    {
        var scene = new Scene("component-events");
        var calls = new List<string>();
        scene.Systems.Add(new PositionObserverSystem(calls));

        var entity = scene.CreateEntity();
        entity.Add(new Position(1, 2));
        entity.Add(new Position(3, 4));
        entity.Remove<Position>();

        Assert.Equal(["add:1,2", "update:3,4", "remove:3,4"], calls);
    }

    [Fact]
    public void ComponentAddedSystemCanDeriveComposition()
    {
        var scene = new Scene("derived-composition");
        scene.Systems.Add(new ViewBindingSystem());

        var entity = scene.CreateEntity()
            .Add(new ViewRequest("hero"));

        Assert.True(entity.Has<ViewBound>());
        Assert.Equal("hero", entity.Get<ViewBound>().Location);
    }

    [Fact]
    public void Component_store_dump_blocks_export_entity_ids_and_values_by_component_type()
    {
        var scene = new Scene("dump-blocks");
        var first = scene.CreateEntity()
            .Add(new Position(1, 2))
            .Add(new Velocity(3, 4));
        var second = scene.CreateEntity()
            .Add(new Position(5, 6));

        var blocks = scene.ComponentStoreDumpBlocks;

        var positionBlock = Assert.Single(blocks, block => block.ComponentType == typeof(Position));
        var velocityBlock = Assert.Single(blocks, block => block.ComponentType == typeof(Velocity));
        var positions = Assert.IsType<Position[]>(positionBlock.Values);
        var velocities = Assert.IsType<Velocity[]>(velocityBlock.Values);
        Assert.Equal([first.Id.Value, second.Id.Value], positionBlock.EntityIds);
        Assert.Equal([first.Id.Value], velocityBlock.EntityIds);
        Assert.Equal(1, positions[0].X);
        Assert.Equal(6, positions[1].Y);
        Assert.Equal(3, velocities[0].X);
    }

    [Fact]
    public void Component_store_dump_blocks_are_odin_serializable_without_component_specific_code()
    {
        var scene = new Scene("dump-blocks");
        var entity = scene.CreateEntity()
            .Add(new Position(1, 2));
        var block = Assert.Single(scene.ComponentStoreDumpBlocks);
        var serializer = new OdinGameSerializer();

        var payload = serializer.SerializeToBytes(block);
        var restored = serializer.DeserializeFromBytes<EcsComponentStoreDumpBlock>(payload);

        Assert.NotNull(restored);
        Assert.Equal(typeof(Position), restored.ComponentType);
        Assert.Equal([entity.Id.Value], restored.EntityIds);
        var values = Assert.IsType<Position[]>(restored.Values);
        Assert.Equal(1, values[0].X);
        Assert.Equal(2, values[0].Y);
    }

    [Fact]
    public void Component_type_debug_view_lists_entity_components_without_exporting_values()
    {
        var scene = new Scene("debug-types");
        var entity = scene.CreateEntity()
            .Add(new Position(1, 2))
            .Add(new Velocity(3, 4));

        var componentTypes = scene.GetComponentTypes(entity);

        Assert.Contains(typeof(Position), componentTypes);
        Assert.Contains(typeof(Velocity), componentTypes);
        Assert.True(scene.TryGetComponent(entity, typeof(Position), out var component));
        var position = Assert.IsType<Position>(component);
        Assert.Equal(1, position.X);
        Assert.False(scene.TryGetComponent(entity, typeof(ViewBound), out _));
    }

    private sealed class MovementSystem : QuerySystem<Position, Velocity>
    {
        protected override void OnUpdate(EntityQuery<Position, Velocity> query, in SystemUpdateContext context)
        {
            var deltaTime = context.DeltaTime;
            query.ForEach((ref Position position, ref Velocity velocity, Entity _) =>
            {
                position.X += velocity.X * deltaTime;
                position.Y += velocity.Y * deltaTime;
            });
        }
    }

    private sealed class RecordingSystem(string name, int order, List<string> calls) : EcsSystem(order)
    {
        public override void Update(Scene scene, in SystemUpdateContext context)
        {
            calls.Add(name);
        }
    }

    private sealed class LifecycleSystem(List<string> calls) : EcsSystem
    {
        protected override void OnCreate(Scene scene)
        {
            calls.Add("create");
        }

        protected override void OnStartRunning(Scene scene)
        {
            calls.Add("start");
        }

        public override void Update(Scene scene, in SystemUpdateContext context)
        {
            calls.Add("update");
        }

        protected override void OnStopRunning(Scene scene)
        {
            calls.Add("stop");
        }

        protected override void OnDestroy(Scene scene)
        {
            calls.Add("destroy");
        }
    }

    private sealed class PositionObserverSystem(List<string> calls) : EcsSystem,
        IComponentAddedSystem<Position>,
        IComponentUpdatedSystem<Position>,
        IComponentRemovedSystem<Position>
    {
        public override void Update(Scene scene, in SystemUpdateContext context)
        {
        }

        public void OnComponentAdded(Scene scene, Entity entity, ref Position component)
        {
            calls.Add($"add:{component.X},{component.Y}");
        }

        public void OnComponentUpdated(Scene scene, Entity entity, ref Position component)
        {
            calls.Add($"update:{component.X},{component.Y}");
        }

        public void OnComponentRemoved(Scene scene, Entity entity, in Position component)
        {
            calls.Add($"remove:{component.X},{component.Y}");
        }
    }

    private sealed class ViewBindingSystem : EcsSystem, IComponentAddedSystem<ViewRequest>
    {
        public override void Update(Scene scene, in SystemUpdateContext context)
        {
        }

        public void OnComponentAdded(Scene scene, Entity entity, ref ViewRequest component)
        {
            entity.Add(new ViewBound(component.Location));
        }
    }

    private struct Position(double x, double y) : IComponent
    {
        public double X = x;

        public double Y = y;
    }

    private readonly struct Velocity(double x, double y) : IComponent
    {
        public double X { get; } = x;

        public double Y { get; } = y;
    }

    private readonly struct ViewRequest(string location) : IComponent
    {
        public string Location { get; } = location;
    }

    private readonly struct ViewBound(string location) : IComponent
    {
        public string Location { get; } = location;
    }
}
