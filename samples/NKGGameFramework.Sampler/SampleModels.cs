using NKGGameFramework.Ecs;

namespace NKGGameFramework.Sampler;

internal readonly record struct GameConfig(string PlayerName, int MaxFrames);

// 存档 DTO 不需要 attribute，也不需要生成代码；Odin 会按当前策略处理普通 C# 类型。
internal sealed class GameSnapshot
{
    public int Frame { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public int Health { get; set; }
}

// 组件都是纯数据 struct，不持有引擎对象。
// 行为放到 System 中，数据组合放到 Entity 上。
internal struct PlayerTag : IComponent;

internal struct Position(double x, double y) : IComponent
{
    public double X = x;

    public double Y = y;
}

internal readonly struct Velocity(double x, double y) : IComponent
{
    public double X { get; } = x;

    public double Y { get; } = y;
}

internal struct Health(int value) : IComponent
{
    public int Value = value;
}

internal readonly struct Presentation(string viewName) : IComponent
{
    public string ViewName { get; } = viewName;
}
