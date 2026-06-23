namespace NKGGameFramework.Gameplay;

public interface ISkillEffect
{
    void Execute(SkillExecutionContext context, SkillEffectDefinition effect);
}

public sealed class ApplyBuffSkillEffect : ISkillEffect
{
    public static readonly ApplyBuffSkillEffect Instance = new();

    public void Execute(SkillExecutionContext context, SkillEffectDefinition effect)
    {
        if (effect.Buff is null)
        {
            throw new InvalidOperationException("Apply-buff skill effect requires a BuffDefinition.");
        }

        BuffManager.Apply(context.Caster, context.Target, effect.Buff, context.Slot.Level);
    }
}
