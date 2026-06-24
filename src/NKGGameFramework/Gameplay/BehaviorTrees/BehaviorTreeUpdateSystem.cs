using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class BehaviorTreeUpdateSystem : EcsSystem
{
    private readonly List<BehaviorTreeInstance> _instancesToUpdate = [];

    public BehaviorTreeUpdateSystem(int order = 0)
        : base(order)
    {
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var time = context.Time;
        _instancesToUpdate.Clear();
        try
        {
            scene.Query<BehaviorTreeComponent>().ForEach((ref BehaviorTreeComponent component, Entity _) =>
            {
                _instancesToUpdate.AddRange(component.MutableInstances);
            });

            foreach (var instance in _instancesToUpdate)
            {
                instance.Update(in time);
            }
        }
        finally
        {
            _instancesToUpdate.Clear();
        }

        scene.Query<BehaviorTreeComponent>().ForEach((ref BehaviorTreeComponent component, Entity _) =>
        {
            var instances = component.MutableInstances;
            for (var index = instances.Count - 1; index >= 0; index--)
            {
                if (instances[index].IsComplete)
                {
                    instances[index].Dispose();
                    instances.RemoveAt(index);
                }
            }
        });
    }
}
