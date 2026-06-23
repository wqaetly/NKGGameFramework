using NKGGameFramework.Ecs;

namespace NKGGameFramework.Ecs.Tests;

public sealed class WorldIsolationTests
{
    [Fact]
    public void WorldsUpdateTheirOwnEntitiesOnly()
    {
        var battle = new World("battle");
        var battleScene = battle.CreateScene("main");
        var preview = new World("preview");
        var previewScene = preview.CreateScene("main");
        battleScene.Systems.Add(new MovementSystem());
        previewScene.Systems.Add(new MovementSystem());

        var battleEntity = battleScene.CreateEntity()
            .Add(new Position(0, 0))
            .Add(new Velocity(10, 0));
        var previewEntity = previewScene.CreateEntity()
            .Add(new Position(100, 0))
            .Add(new Velocity(10, 0));

        battle.Update(0.5, 0.5);

        Assert.Equal(5, battleEntity.Get<Position>().X);
        Assert.Equal(100, previewEntity.Get<Position>().X);
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
}
