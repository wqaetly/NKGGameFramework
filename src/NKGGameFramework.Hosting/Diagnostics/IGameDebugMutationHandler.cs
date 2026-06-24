namespace NKGGameFramework.Hosting.Diagnostics;

public interface IGameDebugMutationHandler
{
    GameDebugMutationResult Execute(GameDebugMutationRequest request);
}
