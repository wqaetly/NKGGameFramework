using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class BehaviorBlackboardPoolComponent : ISceneComponent, IDisposable
{
    private readonly BehaviorBlackboardValuePool _valuePool = new();

    internal BehaviorBlackboardValuePool ValuePool => _valuePool;

    public void Dispose()
    {
        _valuePool.Clear();
    }
}
