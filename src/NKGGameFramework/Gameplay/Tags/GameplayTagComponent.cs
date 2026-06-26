using NKGGameFramework.Ecs;
using OdinSerializer;

namespace NKGGameFramework.Gameplay;

public struct GameplayTagComponent : IComponent
{
    [OdinSerialize]
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
