using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public readonly struct BehaviorActionContext
{
    internal BehaviorActionContext(
        BehaviorTreeInstance tree,
        BehaviorActionNode node,
        BehaviorActionRequest request,
        in GameFrameTime time)
    {
        Tree = tree;
        Node = node;
        Request = request;
        Time = time;
    }

    public BehaviorTreeInstance Tree { get; }

    public BehaviorActionNode Node { get; }

    public BehaviorActionRequest Request { get; }

    public GameFrameTime Time { get; }

    public TimeSpan DeltaTime => Time.DeltaTime;

    public BehaviorBlackboard Blackboard => Tree.Blackboard;

    public BehaviorTreeContext Context => Tree.Context;

    public Scene? Scene => Context.Scene;

    public Entity? Owner => Context.Owner;

    public Entity? Target => Context.Target;

    public SkillSlot? SkillSlot => Context.SkillSlot;

    public BuffInstance? BuffInstance => Context.BuffInstance;

    public BuffDefinition? Buff => Node.Buff;

    public IReadOnlyDictionary<string, string> Parameters => Node.Parameters;
}

public interface IBehaviorAction
{
    BehaviorActionStatus Execute(in BehaviorActionContext context);
}

public sealed class DelegateBehaviorAction : IBehaviorAction
{
    private readonly Func<BehaviorActionContext, BehaviorActionStatus> _execute;

    public DelegateBehaviorAction(Func<BehaviorActionContext, BehaviorActionStatus> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public BehaviorActionStatus Execute(in BehaviorActionContext context)
    {
        return _execute(context);
    }
}

public sealed class BehaviorActionRegistry
{
    private readonly Dictionary<string, IBehaviorAction> _actions = new(StringComparer.Ordinal);

    public static BehaviorActionRegistry CreateDefault(SkillEffectRegistry? skillEffects = null)
    {
        var registry = new BehaviorActionRegistry();
        registry.Register(BehaviorActionKeys.ApplyBuff, ApplyBuffBehaviorAction.Instance);
        registry.Register(BehaviorActionKeys.ApplySkillEffects, new ApplySkillEffectsBehaviorAction(skillEffects));
        return registry;
    }

    public BehaviorActionRegistry Register(string key, IBehaviorAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(action);

        _actions[key] = action;
        return this;
    }

    public bool TryResolve(string key, out IBehaviorAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _actions.TryGetValue(key, out action!);
    }

    public IBehaviorAction Resolve(string key)
    {
        if (TryResolve(key, out var action))
        {
            return action;
        }

        throw new KeyNotFoundException($"Behavior action '{key}' is not registered.");
    }
}

public static class BehaviorActionKeys
{
    public const string ApplyBuff = "apply_buff";
    public const string ApplySkillEffects = "apply_skill_effects";
}

internal sealed class ApplyBuffBehaviorAction : IBehaviorAction
{
    public static readonly ApplyBuffBehaviorAction Instance = new();

    public BehaviorActionStatus Execute(in BehaviorActionContext context)
    {
        if (context.Request != BehaviorActionRequest.Start)
        {
            return BehaviorActionStatus.Success;
        }

        if (context.Owner is not { } owner || context.Target is not { } target)
        {
            throw new InvalidOperationException("Apply-buff behavior action requires owner and target entities.");
        }

        if (context.Buff is null)
        {
            throw new InvalidOperationException("Apply-buff behavior action requires a BuffDefinition payload.");
        }

        BuffManager.Apply(owner, target, context.Buff, context.SkillSlot?.Level ?? context.BuffInstance?.Level ?? 1);
        return BehaviorActionStatus.Success;
    }
}

internal sealed class ApplySkillEffectsBehaviorAction : IBehaviorAction
{
    private readonly SkillEffectRegistry? _skillEffects;

    public ApplySkillEffectsBehaviorAction(SkillEffectRegistry? skillEffects)
    {
        _skillEffects = skillEffects;
    }

    public BehaviorActionStatus Execute(in BehaviorActionContext context)
    {
        if (context.Request != BehaviorActionRequest.Start)
        {
            return BehaviorActionStatus.Success;
        }

        if (context.Scene is not { } scene || context.Owner is not { } caster || context.Target is not { } target || context.SkillSlot is not { } slot)
        {
            throw new InvalidOperationException("Apply-skill-effects behavior action requires a skill execution context.");
        }

        var effects = _skillEffects ?? SkillEffectRegistry.CreateDefault();
        var executionContext = new SkillExecutionContext(scene, caster, target, slot);
        foreach (var effectDefinition in slot.Definition.Effects)
        {
            if (!effects.TryResolve(effectDefinition.Key, out var effect))
            {
                throw new KeyNotFoundException($"Skill effect '{effectDefinition.Key}' is not registered.");
            }

            effect.Execute(executionContext, effectDefinition);
        }

        return BehaviorActionStatus.Success;
    }
}
