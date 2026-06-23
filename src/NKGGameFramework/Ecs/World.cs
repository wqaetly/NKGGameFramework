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

    public void Update(double deltaTime, double realDeltaTime)
    {
        foreach (var scene in _scenes.Values)
        {
            scene.Update(deltaTime, realDeltaTime);
        }
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
