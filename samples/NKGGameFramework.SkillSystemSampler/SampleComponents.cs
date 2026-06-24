using NKGGameFramework.Ecs;

namespace NKGGameFramework.SkillSystemSampler;

// 生命值是最小战斗组件，示例通过它直接观察伤害结果。
internal struct Health(int value) : IComponent
{
    public int Value = value;
}

// 火焰抗性用倍率表示；真实伤害会绕过这个组件。
internal readonly struct FireResistance(double fireDamageMultiplier) : IComponent
{
    public double FireDamageMultiplier { get; } = fireDamageMultiplier;
}

// 连段状态挂在施法者身上，每次释放后下一次多发射一枚火球。
internal struct FireballComboComponent(int nextProjectileCount, int maxProjectileCount) : IComponent
{
    public int NextProjectileCount = nextProjectileCount;

    public int MaxProjectileCount = maxProjectileCount;
}
