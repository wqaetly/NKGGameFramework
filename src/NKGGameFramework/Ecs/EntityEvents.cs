namespace NKGGameFramework.Ecs;

public readonly record struct EntityCreated(EntityRef Entity);

public readonly record struct EntityDestroyed(EntityRef Entity);

public readonly record struct ComponentAdded<TComponent>(EntityRef Entity)
    where TComponent : struct, IComponent;

public readonly record struct ComponentUpdated<TComponent>(EntityRef Entity)
    where TComponent : struct, IComponent;

public readonly record struct ComponentRemoved<TComponent>(EntityRef Entity)
    where TComponent : struct, IComponent;

