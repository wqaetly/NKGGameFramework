using NKGGameFramework.Ecs;

namespace NKGGameFramework.GodotPlaneSample;

internal sealed class PlayerInputSystem : EcsSystem
{
    public PlayerInputSystem()
        : base(order: 0)
    {
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var input = scene.GetOrCreateSceneComponent<PlaneInputState>();
        var delta = context.DeltaTime;
        var query = scene.Query<PlayerTag, Position>();
        query.ForEach((ref PlayerTag _, ref Position position, Entity __) =>
        {
            var diagonalScale = input.MoveX != 0 && input.MoveY != 0 ? 0.7071d : 1.0d;
            position.X = PlaneGameRules.Clamp(position.X + input.MoveX * PlaneGameRules.PlayerSpeed * diagonalScale * delta, 30, PlaneGameRules.ArenaWidth - 30);
            position.Y = PlaneGameRules.Clamp(position.Y + input.MoveY * PlaneGameRules.PlayerSpeed * diagonalScale * delta, 62, PlaneGameRules.ArenaHeight - 28);
        });
    }
}

internal sealed class EnemyPatternSystem : EcsSystem
{
    public EnemyPatternSystem()
        : base(order: 10)
    {
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var frame = context.Frame;

        scene.Query<EnemyTag, Position>().ForEach((ref EnemyTag enemy, ref Position position, Entity _) =>
        {
            position.X = PlaneGameRules.Clamp(
                enemy.BaseX + Math.Sin(frame * enemy.DriftSpeed + enemy.Lane * 0.71d) * enemy.Amplitude,
                30,
                PlaneGameRules.ArenaWidth - 30);

            if (position.Y > PlaneGameRules.ArenaHeight + 32)
            {
                position.Y = -48 - enemy.Lane % 6 * 18;
            }
        });
    }
}

internal sealed class EnemySpawnSystem : EcsSystem
{
    public EnemySpawnSystem()
        : base(order: 15)
    {
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var count = 0;
        scene.Query<EnemyTag>().ForEach((ref EnemyTag _, Entity __) =>
        {
            count++;
        });

        if (count >= PlaneGameRules.TargetEnemyCount)
        {
            return;
        }

        var state = scene.GetOrCreateSceneComponent<PlaneGameState>();
        while (count < PlaneGameRules.TargetEnemyCount)
        {
            PlaneGameRules.SpawnEnemy(scene, state, -60 - count * 32);
            count++;
        }
    }
}

internal sealed class BulletSpawnerSystem : EcsSystem
{
    public BulletSpawnerSystem()
        : base(order: 20)
    {
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var state = scene.GetOrCreateSceneComponent<PlaneGameState>();
        var input = scene.GetOrCreateSceneComponent<PlaneInputState>();
        state.Frame++;

        if (state.FireCooldown > 0)
        {
            state.FireCooldown--;
        }

        if (!input.Fire || state.FireCooldown > 0)
        {
            return;
        }

        var playerPosition = default(Position);
        var foundPlayer = false;

        scene.Query<PlayerTag, Position>().ForEach((ref PlayerTag _, ref Position position, Entity _) =>
        {
            playerPosition = position;
            foundPlayer = true;
        });

        if (!foundPlayer)
        {
            return;
        }

        state.FireCooldown = PlaneGameRules.FireCooldownFrames;
        scene.CreateEntity()
            .Add(new BulletTag())
            .Add(new Position(playerPosition.X, playerPosition.Y - 24))
            .Add(new Velocity(0, -PlaneGameRules.BulletSpeed))
            .Add(new Bounds(6));
    }
}

internal sealed class MovementSystem : QuerySystem<Position, Velocity>
{
    public MovementSystem()
        : base(order: 30)
    {
    }

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

internal sealed class CollisionSystem : EcsSystem
{
    public CollisionSystem()
        : base(order: 40)
    {
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var state = scene.GetOrCreateSceneComponent<PlaneGameState>();
        var commands = context.Commands;
        var enemies = new List<(Entity Entity, Position Position, double Radius)>();

        scene.Query<EnemyTag, Position>().ForEach((ref EnemyTag _, ref Position position, Entity entity) =>
        {
            ref var bounds = ref entity.Get<Bounds>();
            enemies.Add((entity, position, bounds.Radius));
        });

        scene.Query<BulletTag, Position>().ForEach((ref BulletTag _, ref Position bulletPosition, Entity bullet) =>
        {
            if (bulletPosition.Y < -24)
            {
                commands.Destroy(bullet);
                return;
            }

            ref var bulletBounds = ref bullet.Get<Bounds>();
            for (var i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                var dx = enemy.Position.X - bulletPosition.X;
                var dy = enemy.Position.Y - bulletPosition.Y;
                var hitRadius = enemy.Radius + bulletBounds.Radius;
                if (dx * dx + dy * dy > hitRadius * hitRadius)
                {
                    continue;
                }

                state.Score += 100;
                commands.Destroy(bullet);
                commands.Destroy(enemy.Entity);
                break;
            }
        });

        scene.Query<PlayerTag, Position>().ForEach((ref PlayerTag _, ref Position playerPosition, Entity player) =>
        {
            ref var playerBounds = ref player.Get<Bounds>();
            for (var i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                var dx = enemy.Position.X - playerPosition.X;
                var dy = enemy.Position.Y - playerPosition.Y;
                var hitRadius = enemy.Radius + playerBounds.Radius;
                if (dx * dx + dy * dy > hitRadius * hitRadius)
                {
                    continue;
                }

                state.Lives--;
                commands.Destroy(enemy.Entity);
                break;
            }
        });
    }
}
