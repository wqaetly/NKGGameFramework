using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public readonly struct SkillExecutionContext
{
    internal SkillExecutionContext(Scene scene, Entity caster, Entity target, SkillSlot slot)
    {
        Scene = scene;
        Caster = caster;
        Target = target;
        Slot = slot;
    }

    public Scene Scene { get; }

    public Entity Caster { get; }

    public Entity Target { get; }

    public SkillSlot Slot { get; }

    public SkillDefinition Skill => Slot.Definition;
}
