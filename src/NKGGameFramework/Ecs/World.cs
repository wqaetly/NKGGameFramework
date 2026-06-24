using NKGGameFramework.Core;

namespace NKGGameFramework.Ecs;

public sealed class World : IDisposable
{
    private readonly Dictionary<string, Scene> _scenes = new(StringComparer.Ordinal);

    public World(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public GameFrameTime Time { get; private set; } = GameFrameTime.Zero;

    public IReadOnlyCollection<Scene> Scenes => _scenes.Values;

    public Scene CreateScene(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_scenes.ContainsKey(name))
        {
            throw new InvalidOperationException($"Scene '{name}' already exists in world '{Name}'.");
        }

        var scene = new Scene(name);
        _scenes.Add(name, scene);
        return scene;
    }

    public bool TryGetScene(string name, out Scene? scene)
    {
        return _scenes.TryGetValue(name, out scene);
    }

    public void Update(in GameFrameTime time)
    {
        Time = time;

        foreach (var scene in _scenes.Values)
        {
            scene.Update(in time);
        }
    }

    public void Update(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.Advance(Time, deltaTime, realDeltaTime);
        Update(in time);
    }

    public void Dispose()
    {
        foreach (var scene in _scenes.Values)
        {
            scene.Dispose();
        }

        _scenes.Clear();
    }
}
