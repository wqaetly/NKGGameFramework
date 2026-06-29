using NKGGameFramework.Ecs;

namespace NKGGameFramework.GodotPlaneSample;

internal sealed class PlaneGameState : ISceneComponent
{
    public int Frame { get; set; }

    public int Score { get; set; }

    public int Lives { get; set; } = 5;

    public int FireCooldown { get; set; }

    public int NextEnemyLane { get; set; }
}

internal sealed class PlaneInputState : ISceneComponent
{
    public int MoveX { get; set; }

    public int MoveY { get; set; }

    public bool Fire { get; set; }
}

internal readonly record struct PlayerTag : IComponent;

internal readonly record struct EnemyTag(int Lane, double BaseX, double DriftSpeed, double Amplitude) : IComponent;

internal readonly record struct BulletTag : IComponent;

internal struct Position(double x, double y) : IComponent
{
    public double X = x;

    public double Y = y;
}

internal struct Velocity(double x, double y) : IComponent
{
    public double X = x;

    public double Y = y;
}

internal readonly record struct Bounds(double Radius) : IComponent;
