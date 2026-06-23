using NKGGameFramework.Runtime;

namespace NKGGameFramework.Adapter.Unity;

public interface IUnityGameLoopDriver
{
    void Tick(double deltaTime, double realDeltaTime);
}

public interface IUnityAssetService : IAssetService
{
}

public interface IUnitySceneService : ISceneService
{
}

