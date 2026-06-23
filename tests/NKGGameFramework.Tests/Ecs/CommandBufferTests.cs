using NKGGameFramework.Core;
using NKGGameFramework.Ecs;
using System.Reflection;

namespace NKGGameFramework.Ecs.Tests;

public sealed class CommandBufferTests
{
    [Fact]
    public void StructuralChangesDuringQueryMustUseCommandBuffer()
    {
        var scene = new Scene("structural");
        var entity = scene.CreateEntity()
            .Add(new Position(0));

        Assert.Throws<InvalidOperationException>(() =>
        {
            scene.Query<Position>().ForEach((ref Position _, Entity queried) =>
            {
                queried.Add(new Velocity(1));
            });
        });

        var commands = scene.CreateCommandBuffer();
        scene.Query<Position>().ForEach((ref Position _, Entity queried) =>
        {
            commands.Add(queried, new Velocity(1));
        });
        commands.Playback();

        Assert.True(entity.Has<Velocity>());
    }

    [Fact]
    public void CommandBuffersAreReturnedToScenePoolAfterPlayback()
    {
        var scene = new Scene("pool");
        var entity = scene.CreateEntity();

        var first = scene.CreateCommandBuffer();
        first.Add(entity, new Position(3));
        first.Playback();

        Assert.Throws<InvalidOperationException>(() => first.Destroy(entity));

        var reused = scene.CreateCommandBuffer();

        Assert.Same(first, reused);

        reused.Dispose();
    }

    [Fact]
    public void CommandBuffersAreReturnedToScenePoolWhenDisposedWithoutPlayback()
    {
        var scene = new Scene("dispose");

        var first = scene.CreateCommandBuffer();
        first.Dispose();

        var reused = scene.CreateCommandBuffer();

        Assert.Same(first, reused);

        reused.Dispose();
    }

    [Fact]
    public void CommandTypesArePoolItems()
    {
        var commandTypes = typeof(EcsCommandBuffer)
            .GetNestedTypes(BindingFlags.NonPublic)
            .Where(static type => type.Name.EndsWith("Command") || type.Name.StartsWith("AddComponentCommand") || type.Name.StartsWith("RemoveComponentCommand"))
            .ToArray();

        Assert.NotEmpty(commandTypes);
        Assert.All(commandTypes, static type => Assert.True(typeof(IPoolItem).IsAssignableFrom(type), $"{type.Name} should implement IPoolItem."));
    }

    [Fact]
    public void LifecycleEventsAreScopedToSceneEventBus()
    {
        var scene = new Scene("events");
        var added = 0;
        var removed = 0;
        var destroyed = 0;
        scene.Events.Subscribe<ComponentAdded<Position>>(_ => added++);
        scene.Events.Subscribe<ComponentRemoved<Position>>(_ => removed++);
        scene.Events.Subscribe<EntityDestroyed>(_ => destroyed++);

        var entity = scene.CreateEntity()
            .Add(new Position(1));
        entity.Destroy();

        Assert.Equal(1, added);
        Assert.Equal(1, removed);
        Assert.Equal(1, destroyed);
    }

    private struct Position(double x) : IComponent
    {
        public double X = x;
    }

    private readonly struct Velocity(double x) : IComponent
    {
        public double X { get; } = x;
    }
}
