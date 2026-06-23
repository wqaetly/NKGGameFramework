using NKGGameFramework.Runtime;

namespace NKGGameFramework.Adapter.Godot;

public interface IGodotGameLoopDriver
{
    void Process(double deltaTime);
}

public interface IGodotAssetService : IAssetService
{
}

public interface IGodotSceneService : ISceneService
{
}

