using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class SkillCooldownSystem : EcsSystem
{
    public SkillCooldownSystem(int order = 0)
        : base(order)
    {
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var deltaTime = TimeSpan.FromSeconds(Math.Max(0, context.DeltaTime));

        scene.Query<SkillBookComponent>().ForEach((ref SkillBookComponent book, Entity _) =>
        {
            foreach (var slot in book.MutableSkills.Values)
            {
                slot.Tick(deltaTime);
            }
        });
    }
}
