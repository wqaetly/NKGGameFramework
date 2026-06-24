using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class BehaviorTreeUpdateSystem : EcsSystem
{
    public BehaviorTreeUpdateSystem(int order = 0)
        : base(order)
    {
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var time = context.Time;
        var instancesToUpdate = new List<BehaviorTreeInstance>();

        scene.Query<BehaviorTreeComponent>().ForEach((ref BehaviorTreeComponent component, Entity _) =>
        {
            instancesToUpdate.AddRange(component.MutableInstances);
        });

        foreach (var instance in instancesToUpdate)
        {
            instance.Update(in time);
        }

        scene.Query<BehaviorTreeComponent>().ForEach((ref BehaviorTreeComponent component, Entity _) =>
        {
            var instances = component.MutableInstances;
            for (var i = instances.Count - 1; i >= 0; i--)
            {
                if (instances[i].IsComplete)
                {
                    instances.RemoveAt(i);
                }
            }
        });
    }
}
