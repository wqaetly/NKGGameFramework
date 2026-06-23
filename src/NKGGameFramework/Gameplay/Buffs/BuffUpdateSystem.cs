using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class BuffUpdateSystem : EcsSystem
{
    private readonly BuffEffectRegistry _effects;

    public BuffUpdateSystem(BuffEffectRegistry? effects = null, int order = 0)
        : base(order)
    {
        _effects = effects ?? BuffEffectRegistry.CreateDefault();
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var deltaTime = TimeSpan.FromSeconds(Math.Max(0, context.DeltaTime));

        scene.Query<BuffCollectionComponent>().ForEach((ref BuffCollectionComponent collection, Entity entity) =>
        {
            var buffs = collection.MutableBuffs;
            for (var i = buffs.Count - 1; i >= 0; i--)
            {
                var buff = buffs[i];
                var effect = _effects.Resolve(buff.Definition.EffectKey);
                var effectContext = new BuffEffectContext(scene, entity, buff, deltaTime);

                ProcessBuff(scene, effect, effectContext);

                if (buff.State == BuffState.Finished)
                {
                    effect.OnRemove(effectContext);
                    scene.Events.Publish(new BuffRemoved(
                        entity.ToRef(),
                        buff.Source,
                        buff.Definition.Id,
                        buff.Level,
                        buff.Stacks));
                    buffs.RemoveAt(i);
                }
            }
        });
    }

    private static void ProcessBuff(Scene scene, IBuffEffect effect, BuffEffectContext context)
    {
        var buff = context.Buff;

        if (buff.State == BuffState.Waiting)
        {
            effect.OnApply(context);
            scene.Events.Publish(new BuffApplied(
                context.Target.ToRef(),
                buff.Source,
                buff.Definition.Id,
                buff.Level,
                buff.Stacks));

            buff.State = buff.Definition.Duration switch
            {
                null => BuffState.Forever,
                { } duration when duration == TimeSpan.Zero => BuffState.Finished,
                _ => BuffState.Running,
            };
        }
        else if (buff.PendingRefresh)
        {
            buff.ClearPendingRefresh();
            effect.OnRefresh(context);
            scene.Events.Publish(new BuffRefreshed(
                context.Target.ToRef(),
                buff.Source,
                buff.Definition.Id,
                buff.Level,
                buff.Stacks));
        }

        if (buff.State is BuffState.Running or BuffState.Forever)
        {
            effect.OnUpdate(context);
            buff.Tick(context.DeltaTime);
        }
    }
}
