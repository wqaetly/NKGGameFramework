namespace NKGGameFramework.Diagnostics;

public interface IGameDebugSnapshotProvider
{
    GameDebugSnapshot Capture(GameDebugSnapshotCaptureOptions? options = null);
}
