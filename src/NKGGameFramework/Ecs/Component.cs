namespace NKGGameFramework.Ecs;

// Components are value types by design. Keep them on generic paths such as
// ComponentStore<TComponent> and EntityQuery<TComponent>; storing them as
// IComponent or object would box the struct and defeat the ECS data layout.
public interface IComponent
{
}

public interface ITag : IComponent
{
}
