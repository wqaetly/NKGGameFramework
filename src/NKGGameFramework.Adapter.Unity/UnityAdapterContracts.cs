using NKGGameFramework.Core;
using NKGGameFramework.Runtime;

namespace NKGGameFramework.Adapter.Unity;

public interface IUnityGameLoopDriver
{
    void Tick(in GameFrameTime time);

    void Tick(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.FromSeconds(deltaTime, realDeltaTime);
        Tick(in time);
    }
}

public interface IUnityAssetService : IAssetService
{
}

public interface IUnitySceneService : ISceneService
{
}
