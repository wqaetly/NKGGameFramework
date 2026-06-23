using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public struct SkillBookComponent : IComponent
{
    private Dictionary<string, SkillSlot>? _skills;

    public SkillBookComponent()
    {
        _skills = new Dictionary<string, SkillSlot>(StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, SkillSlot> Skills => MutableSkills;

    internal Dictionary<string, SkillSlot> MutableSkills => _skills ??= new Dictionary<string, SkillSlot>(StringComparer.Ordinal);

    public bool TryGet(string skillId, out SkillSlot slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        return MutableSkills.TryGetValue(skillId, out slot!);
    }
}
