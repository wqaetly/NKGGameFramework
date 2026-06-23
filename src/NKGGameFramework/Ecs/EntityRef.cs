namespace NKGGameFramework.Ecs;

public readonly struct EntityRef
{
    private readonly Scene? _scene;

    internal EntityRef(Scene scene, int id, int version)
    {
        _scene = scene;
        Id = id;
        Version = version;
    }

    public int Id { get; }

    public int Version { get; }

    public bool IsAlive => _scene?.IsAlive(Id, Version) == true;

    public bool TryGet(out Entity entity)
    {
        if (_scene is null)
        {
            entity = default;
            return false;
        }

        return _scene.TryGetEntity(Id, Version, out entity);
    }
}
