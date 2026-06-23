using NKGGameFramework.Ecs;

namespace NKGGameFramework.Sampler;

internal sealed class PresentationBindingSystem(SampleGame game) : EcsSystem, IComponentAddedSystem<PlayerTag>
{
    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        // 这个系统不需要每帧查询；它只关心 PlayerTag 被添加的那一刻。
    }

    public void OnComponentAdded(Scene scene, Entity entity, ref PlayerTag component)
    {
        // 组件添加回调适合做组合补全：玩家标记出现后，自动补上表现层数据。
        entity.Add(new Presentation(game.Config.PlayerName));
        game.Log($"presentation bound: {game.Config.PlayerName}");
    }
}

internal sealed class MovementSystem : QuerySystem<Position, Velocity>
{
    protected override void OnUpdate(EntityQuery<Position, Velocity> query, in SystemUpdateContext context)
    {
        var deltaTime = context.DeltaTime;

        // QuerySystem 只遍历同时拥有 Position 和 Velocity 的实体。
        // ref 参数允许系统直接修改组件数据。
        query.ForEach((ref Position position, ref Velocity velocity, Entity _) =>
        {
            position.X += velocity.X * deltaTime;
            position.Y += velocity.Y * deltaTime;
        });
    }
}

internal sealed class DamageOverTimeSystem : QuerySystem<Health>
{
    protected override void OnUpdate(EntityQuery<Health> query, in SystemUpdateContext context)
    {
        // 这个系统只关心 Health，用来演示单组件查询。
        query.ForEach((ref Health health, Entity _) =>
        {
            health.Value = Math.Max(0, health.Value - 1);
        });
    }
}
