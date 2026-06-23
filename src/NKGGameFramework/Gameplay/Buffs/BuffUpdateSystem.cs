using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class BuffUpdateSystem : EcsSystem
{
    private readonly BuffEffectRegistry _effects;
    private readonly BehaviorActionRegistry _behaviorActions;

    public BuffUpdateSystem(BuffEffectRegistry? effects, int order)
        : this(effects, null, order)
    {
    }

    public BuffUpdateSystem(BuffEffectRegistry? effects = null, BehaviorActionRegistry? behaviorActions = null, int order = 0)
        : base(order)
    {
        _effects = effects ?? BuffEffectRegistry.CreateDefault();
        _behaviorActions = behaviorActions ?? BehaviorActionRegistry.CreateDefault();
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var deltaTime = TimeSpan.FromSeconds(Math.Max(0, context.DeltaTime));
        var behaviorTreesToStart = new List<BehaviorTreeInstance>();
        var behaviorTreesToUpdate = new List<BehaviorTreeInstance>();
        var behaviorTreesToCancel = new List<BehaviorTreeInstance>();

        scene.Query<BuffCollectionComponent>().ForEach((ref BuffCollectionComponent collection, Entity entity) =>
        {
            var buffs = collection.MutableBuffs;
            for (var i = buffs.Count - 1; i >= 0; i--)
            {
                var buff = buffs[i];
                var effect = _effects.Resolve(buff.Definition.EffectKey);
                var effectContext = new BuffEffectContext(scene, entity, buff, deltaTime);

                ProcessBuff(
                    scene,
                    effect,
                    effectContext,
                    _behaviorActions,
                    behaviorTreesToStart,
                    behaviorTreesToUpdate);

                if (buff.State == BuffState.Finished)
                {
                    effect.OnRemove(effectContext);
                    if (buff.ExecutionTreeInstance is { } behaviorTree)
                    {
                        behaviorTreesToCancel.Add(behaviorTree);
                    }

                    buff.ExecutionTreeInstance = null;
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

        foreach (var behaviorTree in behaviorTreesToStart)
        {
            behaviorTree.Start();
        }

        foreach (var behaviorTree in behaviorTreesToUpdate)
        {
            behaviorTree.Update(deltaTime);
        }

        foreach (var behaviorTree in behaviorTreesToCancel)
        {
            behaviorTree.Cancel();
        }
    }

    private static void ProcessBuff(
        Scene scene,
        IBuffEffect effect,
        BuffEffectContext context,
        BehaviorActionRegistry behaviorActions,
        List<BehaviorTreeInstance> behaviorTreesToStart,
        List<BehaviorTreeInstance> behaviorTreesToUpdate)
    {
        var buff = context.Buff;

        if (buff.State == BuffState.Waiting)
        {
            effect.OnApply(context);
            StartExecutionTree(scene, context, behaviorActions, behaviorTreesToStart);
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
            if (buff.ExecutionTreeInstance is { } behaviorTree)
            {
                behaviorTreesToUpdate.Add(behaviorTree);
            }

            buff.Tick(context.DeltaTime);
        }
    }

    private static void StartExecutionTree(
        Scene scene,
        BuffEffectContext context,
        BehaviorActionRegistry behaviorActions,
        List<BehaviorTreeInstance> behaviorTreesToStart)
    {
        var definition = context.Buff.Definition.ExecutionTree;
        if (definition is null)
        {
            return;
        }

        if (!definition.TryValidate(behaviorActions, out var missingActionKey))
        {
            throw new KeyNotFoundException($"Behavior action '{missingActionKey}' is not registered.");
        }

        var source = context.Target;
        if (context.Buff.Source.TryGet(out var resolvedSource))
        {
            source = resolvedSource;
        }

        var behaviorContext = new BehaviorTreeContext(scene, source, context.Target, buffInstance: context.Buff);
        var instance = definition.CreateInstance(behaviorActions, behaviorContext);
        context.Buff.ExecutionTreeInstance = instance;
        behaviorTreesToStart.Add(instance);
    }
}
