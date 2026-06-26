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

    [field: NonSerialized]
    public Scene? Scene { get; }

    [field: NonSerialized]
    public Entity? Owner { get; }

    [field: NonSerialized]
    public Entity? Target { get; }

    [field: NonSerialized]
    public SkillSlot? SkillSlot { get; }

    [field: NonSerialized]
    public BuffInstance? BuffInstance { get; }

    [field: NonSerialized]
    public object? UserState { get; }
}
