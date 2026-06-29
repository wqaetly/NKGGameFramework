namespace NKGGameFramework.Diagnostics;

public interface IGameDebugMutationHandler
{
    GameDebugMutationResult Execute(GameDebugMutationRequest request);
}
