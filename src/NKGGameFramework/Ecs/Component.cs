namespace NKGGameFramework.Ecs;

// Components are value types by design. Keep them on generic paths such as
// ComponentStore<TComponent> and EntityQuery<TComponent>; storing them as
// IComponent or object would box the struct and defeat the ECS data layout.
public interface IComponent
{
}

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ComponentGraphAttribute : Attribute
{
    public Type? Parent { get; init; }

    public string? Group { get; init; }

    public int Order { get; init; }
}

public interface ITag : IComponent
{
}

public interface ISceneComponent
{
}
