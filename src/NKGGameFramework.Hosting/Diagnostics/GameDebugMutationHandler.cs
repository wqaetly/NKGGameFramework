using Microsoft.Extensions.Options;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugMutationHandler : IGameDebugMutationHandler
{
    private readonly GameDebugSession _session;
    private readonly GameDebugOptions _options;
    private readonly IGameDebugComponentValueSerializer _componentValueSerializer;

    public GameDebugMutationHandler(
        GameDebugSession session,
        IOptions<GameDebugOptions> options,
        IGameDebugComponentValueSerializer componentValueSerializer)
    {
        _session = session;
        _options = options.Value;
        _componentValueSerializer = componentValueSerializer;
    }

    public GameDebugMutationResult Execute(GameDebugMutationRequest request)
    {
        if (!_options.EnableMutations)
        {
            return Failure("Debug mutations are disabled.");
        }

        if (!TryResolveEntity(request, out var scene, out var entity, out var failure))
        {
            return Failure(failure);
        }

        try
        {
            if (!TryResolveComponentType(scene, request, out var componentType))
            {
                return Failure($"Component type '{request.ComponentTypeFullName}' was not found in scene '{request.SceneName}'.");
            }

            var component = _componentValueSerializer.Deserialize(request.Value, componentType);
            scene.SetComponent(entity, componentType, component);
            return Success($"Component '{componentType.Name}' was updated on entity {entity.Id.Value}.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Failure(exception.Message);
        }
    }

    private bool TryResolveEntity(GameDebugMutationRequest request, out Scene scene, out Entity entity, out string failure)
    {
        foreach (var world in _session.GetWorlds())
        {
            if (!StringComparer.Ordinal.Equals(world.Name, request.WorldName))
            {
                continue;
            }

            if (!world.TryGetScene(request.SceneName, out var foundScene) || foundScene is null)
            {
                break;
            }

            scene = foundScene;

            var found = request.EntityVersion is { } version
                ? scene.TryGetEntity(request.EntityId, version, out entity)
                : scene.TryGetEntity(request.EntityId, out entity);

            if (found)
            {
                failure = string.Empty;
                return true;
            }

            failure = $"Entity {request.EntityId} was not found in scene '{request.SceneName}'.";
            return false;
        }

        entity = default;
        scene = null!;
        failure = $"Scene '{request.SceneName}' in world '{request.WorldName}' was not found.";
        return false;
    }

    private static bool TryResolveComponentType(Scene scene, GameDebugMutationRequest request, out Type componentType)
    {
        foreach (var store in scene.ComponentStores)
        {
            if (StringComparer.Ordinal.Equals(store.ComponentType.FullName, request.ComponentTypeFullName)
                && StringComparer.Ordinal.Equals(store.ComponentType.Assembly.GetName().Name, request.ComponentAssemblyName))
            {
                componentType = store.ComponentType;
                return true;
            }
        }

        componentType = null!;
        return false;
    }

    private static GameDebugMutationResult Success(string message)
    {
        return new GameDebugMutationResult(true, message);
    }

    private static GameDebugMutationResult Failure(string message)
    {
        return new GameDebugMutationResult(false, message);
    }
}
