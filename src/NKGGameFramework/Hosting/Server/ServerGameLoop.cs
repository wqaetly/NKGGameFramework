using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Hosting.Server;

public sealed class ServerGameLoop
{
    private readonly RuntimeContext _runtime;
    private readonly World _world;

    public ServerGameLoop(RuntimeContext runtime, World world)
    {
        _runtime = runtime;
        _world = world;
    }

    public void Tick(double deltaTime, double realDeltaTime)
    {
        _runtime.Update(deltaTime, realDeltaTime);
        _world.Update(deltaTime, realDeltaTime);
    }
}

