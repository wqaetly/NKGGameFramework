using NKGGameFramework.Ecs;

namespace NKGGameFramework.GodotPlaneSample;

internal static class PlaneGameRules
{
    public const double ArenaWidth = 640.0d;
    public const double ArenaHeight = 360.0d;
    public const double SimulationHz = 144.0d;
    public const int TargetEnemyCount = 7;
    public const double PlayerSpeed = 155.0d;
    public const double EnemyBaseSpeed = 14.0d;
    public const double EnemySpeedStep = 3.0d;
    public const double EnemyDriftBaseSpeed = 0.010d;
    public const double EnemyDriftSpeedStep = 0.003d;
    public const double BulletSpeed = 340.0d;
    public const int FireCooldownFrames = 58;

    public static Entity SpawnEnemy(Scene scene, PlaneGameState state, double y)
    {
        var lane = state.NextEnemyLane++;
        var laneIndex = lane % 8;
        var baseX = 56 + laneIndex * 76;
        var speed = EnemyBaseSpeed + lane % 5 * EnemySpeedStep;
        var amplitude = 16 + lane % 4 * 9;
        var driftSpeed = EnemyDriftBaseSpeed + lane % 5 * EnemyDriftSpeedStep;

        return scene.CreateEntity()
            .Add(new EnemyTag(lane, baseX, driftSpeed, amplitude))
            .Add(new Position(baseX, y))
            .Add(new Velocity(0, speed))
            .Add(new Bounds(18));
    }

    public static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
