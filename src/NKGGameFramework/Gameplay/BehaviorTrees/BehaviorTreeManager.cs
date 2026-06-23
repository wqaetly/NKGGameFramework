using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public static class BehaviorTreeManager
{
    public static BehaviorTreeInstance Start(Entity owner, BehaviorTreeInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        EnsureComponent(owner);

        ref var component = ref owner.Get<BehaviorTreeComponent>();
        component.MutableInstances.Add(instance);
        instance.Start();
        return instance;
    }

    public static bool CancelAll(Entity owner)
    {
        if (!owner.Has<BehaviorTreeComponent>())
        {
            return false;
        }

        ref var component = ref owner.Get<BehaviorTreeComponent>();
        foreach (var instance in component.MutableInstances)
        {
            instance.Cancel();
        }

        return component.MutableInstances.Count > 0;
    }

    private static void EnsureComponent(Entity owner)
    {
        if (!owner.Has<BehaviorTreeComponent>())
        {
            owner.Add(new BehaviorTreeComponent());
        }
    }
}
