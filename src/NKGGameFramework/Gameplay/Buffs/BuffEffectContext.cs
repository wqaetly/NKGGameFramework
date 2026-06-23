using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public readonly struct BuffEffectContext
{
    internal BuffEffectContext(Scene scene, Entity target, BuffInstance buff, TimeSpan deltaTime)
    {
        Scene = scene;
        Target = target;
        Buff = buff;
        DeltaTime = deltaTime;
    }

    public Scene Scene { get; }

    public IEventBus Events => Scene.Events;

    public Entity Target { get; }

    public BuffInstance Buff { get; }

    public TimeSpan DeltaTime { get; }

    public bool TryGetSource(out Entity source)
    {
        return Buff.Source.TryGet(out source);
    }
}
