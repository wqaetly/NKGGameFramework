using NKGGameFramework.Ecs;

namespace NKGGameFramework.Sampler;

internal sealed class PresentationBindingSystem(SampleGame game) : EcsSystem, IComponentAddedSystem<PlayerTag>
{
    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        // 这个系统不需要每帧查询；它只关心玩家标记被添加的那一刻。
    }

    public void OnComponentAdded(Scene scene, Entity entity, ref PlayerTag component)
    {
        // 组件添加回调适合做组合补全：玩家标记出现后，自动补上表现层数据。
        entity.Add(new Presentation(game.Config.PlayerName));
        game.Log($"表现绑定完成：{game.Config.PlayerName}");
    }
}

internal sealed class MovementSystem : QuerySystem<Position, Velocity>
{
    protected override void OnUpdate(EntityQuery<Position, Velocity> query, in SystemUpdateContext context)
    {
        var deltaTime = context.DeltaTime;

        // 查询系统只遍历同时拥有位置和速度的实体。
        // 引用参数允许系统直接修改组件数据。
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
        // 这个系统只关心生命值，用来演示单组件查询。
        query.ForEach((ref Health health, Entity _) =>
        {
            health.Value = Math.Max(0, health.Value - 1);
        });
    }
}
