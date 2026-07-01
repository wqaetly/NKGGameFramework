using NKGGameFramework.Ecs;

namespace NKGGameFramework.Diagnostics;

internal sealed class GameDebugFrameCapturePipeline
{
    private readonly IGameDebugSnapshotProvider _snapshots;
    private readonly GameDebugController _control;
    private readonly GameDebugSession? _session;
    private readonly object? _debugStateGate;

    public GameDebugFrameCapturePipeline(
        IGameDebugSnapshotProvider snapshots,
        GameDebugController control,
        GameDebugSession? session = null,
        object? debugStateGate = null)
    {
        _snapshots = snapshots;
        _control = control;
        _session = session;
        _debugStateGate = debugStateGate;
    }

    public GameDebugSnapshotMessage CaptureSnapshotMessage(
        GameDebugFrameInfo frame,
        GameDebugSnapshotCaptureOptions captureOptions)
    {
        return Execute(() => CreateSnapshotMessage(frame, captureOptions));
    }

    public GameDebugCapturedDumpFrame CaptureDumpFrame(
        GameDebugFrameInfo frame,
        GameDebugSnapshotCaptureOptions captureOptions,
        bool includeBlocks,
        Func<string, string, EcsComponentStoreDebugView, bool>? storePredicate = null)
    {
        return Execute(() =>
        {
            var message = CreateSnapshotMessage(frame, captureOptions);
            if (!includeBlocks || _session is null)
            {
                return new GameDebugCapturedDumpFrame(message, Blocks: null);
            }

            var blocks = new CapturedDumpFrameBlocks(
                _session.GetWorlds()
                    .Select(world => CaptureWorldBlockInputs(world, storePredicate))
                    .ToArray());

            return new GameDebugCapturedDumpFrame(message, blocks);
        });
    }

    private GameDebugSnapshotMessage CreateSnapshotMessage(
        GameDebugFrameInfo frame,
        GameDebugSnapshotCaptureOptions captureOptions)
    {
        return new GameDebugSnapshotMessage(
            frame,
            _snapshots.Capture(captureOptions),
            _control.GetState());
    }

    private T Execute<T>(Func<T> operation)
    {
        if (_debugStateGate is null)
        {
            return operation();
        }

        lock (_debugStateGate)
        {
            return operation();
        }
    }

    private static CapturedDumpWorldBlocks CaptureWorldBlockInputs(
        World world,
        Func<string, string, EcsComponentStoreDebugView, bool>? storePredicate)
    {
        return new CapturedDumpWorldBlocks(
            world.Name,
            world.Scenes
                .Select(scene => CaptureSceneBlockInputs(world.Name, scene, storePredicate))
                .ToArray());
    }

    private static CapturedDumpSceneBlocks CaptureSceneBlockInputs(
        string worldName,
        Scene scene,
        Func<string, string, EcsComponentStoreDebugView, bool>? storePredicate)
    {
        var componentStores = storePredicate is null
            ? scene.CreateComponentStoreDumpBlocks()
            : scene.CreateComponentStoreDumpBlocks(store => storePredicate(worldName, scene.Name, store));
        return new CapturedDumpSceneBlocks(scene.Name, componentStores.ToArray());
    }
}

internal sealed record GameDebugCapturedDumpFrame(
    GameDebugSnapshotMessage Message,
    CapturedDumpFrameBlocks? Blocks);

internal sealed record CapturedDumpFrameBlocks(
    CapturedDumpWorldBlocks[] Worlds);

internal sealed record CapturedDumpWorldBlocks(
    string Name,
    CapturedDumpSceneBlocks[] Scenes);

internal sealed record CapturedDumpSceneBlocks(
    string Name,
    EcsComponentStoreDumpBlock[] ComponentStores);
