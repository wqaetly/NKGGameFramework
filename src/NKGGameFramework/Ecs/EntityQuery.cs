namespace NKGGameFramework.Ecs;

public delegate void ForEachEntity<TComponent>(ref TComponent component, Entity entity)
    where TComponent : struct, IComponent;

public delegate void ForEachEntity<TFirst, TSecond>(ref TFirst first, ref TSecond second, Entity entity)
    where TFirst : struct, IComponent
    where TSecond : struct, IComponent;

public readonly struct EntityQuery<TComponent>
    where TComponent : struct, IComponent
{
    private readonly Scene _scene;

    internal EntityQuery(Scene scene)
    {
        _scene = scene;
    }

    public void ForEach(ForEachEntity<TComponent> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_scene.TryGetStore<TComponent>(out var store))
        {
            return;
        }

        _scene.EnterQuery();
        try
        {
            var ids = store.EntityIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!_scene.TryGetEntity(id, out var entity))
                {
                    continue;
                }

                ref var component = ref store.Get(id);
                action(ref component, entity);
            }
        }
        finally
        {
            _scene.ExitQuery();
        }
    }
}

public readonly struct EntityQuery<TFirst, TSecond>
    where TFirst : struct, IComponent
    where TSecond : struct, IComponent
{
    private readonly Scene _scene;

    internal EntityQuery(Scene scene)
    {
        _scene = scene;
    }

    public void ForEach(ForEachEntity<TFirst, TSecond> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_scene.TryGetStore<TFirst>(out var firstStore) || !_scene.TryGetStore<TSecond>(out var secondStore))
        {
            return;
        }

        var primaryIds = firstStore.Count <= secondStore.Count ? firstStore.EntityIds : secondStore.EntityIds;

        _scene.EnterQuery();
        try
        {
            for (var i = 0; i < primaryIds.Count; i++)
            {
                var id = primaryIds[i];
                if (!firstStore.Has(id) || !secondStore.Has(id) || !_scene.TryGetEntity(id, out var entity))
                {
                    continue;
                }

                ref var first = ref firstStore.Get(id);
                ref var second = ref secondStore.Get(id);
                action(ref first, ref second, entity);
            }
        }
        finally
        {
            _scene.ExitQuery();
        }
    }
}

