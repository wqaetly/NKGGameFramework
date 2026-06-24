using NKGGameFramework.Core;
using NKGGameFramework.Runtime;

namespace NKGGameFramework.Adapter.Godot;

public interface IGodotGameLoopDriver
{
    void Process(in GameFrameTime time);

    void Process(double deltaTime)
    {
        var time = GameFrameTime.FromSeconds(deltaTime, deltaTime);
        Process(in time);
    }
}

public interface IGodotAssetService : IAssetService
{
}

public interface IGodotSceneService : ISceneService
{
}
