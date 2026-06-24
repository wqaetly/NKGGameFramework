namespace NKGGameFramework.Hosting.Diagnostics;

public interface IGameDebugSnapshotProvider
{
    GameDebugSnapshot Capture();
}
