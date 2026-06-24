namespace NKGGameFramework.Hosting.Diagnostics;

public interface IGameDebugSnapshotProvider
{
    GameDebugSnapshot Capture(GameDebugSnapshotCaptureOptions? options = null);
}
