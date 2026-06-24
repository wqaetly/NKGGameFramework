namespace NKGGameFramework.Core;

public readonly record struct GameFrameTime
{
    public static readonly GameFrameTime Zero = new(0, TimeSpan.Zero, TimeSpan.Zero);

    public GameFrameTime(long frame, TimeSpan deltaTime, TimeSpan realDeltaTime)
    {
        if (frame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Frame cannot be negative.");
        }

        if (deltaTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time cannot be negative.");
        }

        if (realDeltaTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(realDeltaTime), "Real delta time cannot be negative.");
        }

        Frame = frame;
        DeltaTime = deltaTime;
        RealDeltaTime = realDeltaTime;
        DeltaSeconds = deltaTime.TotalSeconds;
        RealDeltaSeconds = realDeltaTime.TotalSeconds;
    }

    public long Frame { get; }

    // Gameplay should use DeltaTime so pause, slow motion, and frame stepping remain deterministic.
    public TimeSpan DeltaTime { get; }

    // RealDeltaTime is for UI, tooling, hosting, networking, profiling, and other systems that must ignore gameplay time scaling.
    public TimeSpan RealDeltaTime { get; }

    public double DeltaSeconds { get; }

    public double RealDeltaSeconds { get; }

    public bool IsPaused => DeltaTime == TimeSpan.Zero && RealDeltaTime > TimeSpan.Zero;

    public static GameFrameTime FromSeconds(double deltaTime, double realDeltaTime, long frame = 0)
    {
        ValidateSeconds(deltaTime, nameof(deltaTime));
        ValidateSeconds(realDeltaTime, nameof(realDeltaTime));
        return new GameFrameTime(frame, TimeSpan.FromSeconds(deltaTime), TimeSpan.FromSeconds(realDeltaTime));
    }

    public static GameFrameTime FromSeconds(double deltaTime, long frame = 0)
    {
        return FromSeconds(deltaTime, deltaTime, frame);
    }

    public static GameFrameTime Advance(GameFrameTime previous, double deltaTime, double realDeltaTime)
    {
        return FromSeconds(deltaTime, realDeltaTime, checked(previous.Frame + 1));
    }

    public static GameFrameTime Advance(GameFrameTime previous, TimeSpan deltaTime, TimeSpan realDeltaTime)
    {
        return new GameFrameTime(checked(previous.Frame + 1), deltaTime, realDeltaTime);
    }

    private static void ValidateSeconds(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Time value must be a finite, non-negative number.");
        }
    }
}
