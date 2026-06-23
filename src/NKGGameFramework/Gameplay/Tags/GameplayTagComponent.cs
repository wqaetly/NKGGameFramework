using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public struct GameplayTagComponent : IComponent
{
    private GameplayTagContainer? _tags;

    public GameplayTagComponent()
    {
        _tags = new GameplayTagContainer();
    }

    public GameplayTagComponent(GameplayTagContainer tags)
    {
        _tags = tags;
    }

    public GameplayTagContainer Tags => _tags ??= new GameplayTagContainer();
}
