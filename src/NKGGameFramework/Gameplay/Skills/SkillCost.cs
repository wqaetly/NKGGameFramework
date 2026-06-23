using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public readonly struct SkillCostContext
{
    internal SkillCostContext(Scene scene, Entity caster, Entity target, SkillSlot slot)
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

    public double Cost => Skill.GetCost(Slot.Level);
}

public interface ISkillCostPolicy
{
    bool CanPay(SkillCostContext context, out string? reason);

    void Pay(SkillCostContext context);
}

public sealed class AllowAllSkillCostPolicy : ISkillCostPolicy
{
    public static readonly AllowAllSkillCostPolicy Instance = new();

    public bool CanPay(SkillCostContext context, out string? reason)
    {
        reason = null;
        return true;
    }

    public void Pay(SkillCostContext context)
    {
    }
}
