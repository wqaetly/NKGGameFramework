using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class BehaviorTreeContext
{
    public BehaviorTreeContext(
        Scene? scene = null,
        Entity? owner = null,
        Entity? target = null,
        SkillSlot? skillSlot = null,
        BuffInstance? buffInstance = null,
        object? userState = null)
    {
        Scene = scene;
        Owner = owner;
        Target = target;
        SkillSlot = skillSlot;
        BuffInstance = buffInstance;
        UserState = userState;
    }

    public Scene? Scene { get; }

    public Entity? Owner { get; }

    public Entity? Target { get; }

    public SkillSlot? SkillSlot { get; }

    public BuffInstance? BuffInstance { get; }

    public object? UserState { get; }
}
