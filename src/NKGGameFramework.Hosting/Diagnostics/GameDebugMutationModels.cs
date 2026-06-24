namespace NKGGameFramework.Hosting.Diagnostics;

public sealed record GameDebugMutationRequest(
    string WorldName,
    string SceneName,
    int EntityId,
    int? EntityVersion,
    string ComponentTypeFullName,
    string ComponentAssemblyName,
    ComponentValueDebugSnapshot Value);

public sealed record GameDebugMutationResult(
    bool Succeeded,
    string Message);
