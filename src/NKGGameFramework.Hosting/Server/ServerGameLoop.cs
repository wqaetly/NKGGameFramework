using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Hosting.Server;

public sealed class ServerGameLoop
{
    private readonly RuntimeContext _runtime;
    private readonly World _world;
    private GameFrameTime _time = GameFrameTime.Zero;

    public ServerGameLoop(RuntimeContext runtime, World world)
    {
        _runtime = runtime;
        _world = world;
    }

    public GameFrameTime Time => _time;

    public void Tick(in GameFrameTime time)
    {
        _time = time;
        _runtime.Update(in time);
        _world.Update(in time);
    }

    public void Tick(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.Advance(_time, deltaTime, realDeltaTime);
        Tick(in time);
    }
}
