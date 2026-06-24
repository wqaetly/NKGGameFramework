using NKGGameFramework.Ecs;

namespace NKGGameFramework.Sampler;

internal readonly record struct GameConfig(string PlayerName, int MaxFrames);

// 存档传输对象不需要特性，也不需要生成代码；序列化器会按当前策略处理普通类型。
internal sealed class GameSnapshot
{
    public int Frame { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public int Health { get; set; }
}

// 组件都是纯数据结构，不持有引擎对象。
// 行为放到系统中，数据组合放到实体上。
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
