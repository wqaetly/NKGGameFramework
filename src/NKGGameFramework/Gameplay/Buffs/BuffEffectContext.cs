using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public readonly struct BuffEffectContext
{
    internal BuffEffectContext(Scene scene, Entity target, BuffInstance buff, in GameFrameTime time)
    {
        Scene = scene;
        Target = target;
        Buff = buff;
        Time = time;
    }

    public Scene Scene { get; }

    public IEventBus Events => Scene.Events;

    public Entity Target { get; }

    public BuffInstance Buff { get; }

    public GameFrameTime Time { get; }

    public TimeSpan DeltaTime => Time.DeltaTime;

    public bool TryGetSource(out Entity source)
    {
        return Buff.Source.TryGet(out source);
    }
}
