using NKGGameFramework.Ecs;
using OdinSerializer;

namespace NKGGameFramework.Gameplay;

[ComponentGraph(Parent = typeof(SkillBookComponent), Group = "Gameplay/Skills", Order = 20)]
public struct BehaviorTreeComponent : IComponent
{
    [OdinSerialize]
    private List<BehaviorTreeInstance>? _instances;

    public BehaviorTreeComponent()
    {
        _instances = [];
    }

    public IReadOnlyList<BehaviorTreeInstance> Instances => MutableInstances;

    public int Count => MutableInstances.Count;

    internal List<BehaviorTreeInstance> MutableInstances => _instances ??= [];
}
