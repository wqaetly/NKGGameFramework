using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

[ComponentGraph(Group = "Gameplay/Buffs", Order = 10)]
public struct BuffCollectionComponent : IComponent
{
    private List<BuffInstance>? _buffs;

    public BuffCollectionComponent()
    {
        _buffs = [];
    }

    public IReadOnlyList<BuffInstance> Buffs => MutableBuffs;

    public int Count => MutableBuffs.Count;

    internal List<BuffInstance> MutableBuffs => _buffs ??= [];

    public bool Has(string buffId)
    {
        return TryGet(buffId, out _);
    }

    public bool TryGet(string buffId, out BuffInstance buff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buffId);

        foreach (var candidate in MutableBuffs)
        {
            if (candidate.Definition.Id == buffId && candidate.State != BuffState.Finished)
            {
                buff = candidate;
                return true;
            }
        }

        buff = null!;
        return false;
    }
}
