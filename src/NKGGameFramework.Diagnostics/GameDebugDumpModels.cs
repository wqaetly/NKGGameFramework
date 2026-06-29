using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Diagnostics;

public sealed record GameDebugDumpDocument(
    string Format,
    int Version,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DroppedFrameCount,
    IReadOnlyList<GameDebugSnapshotMessage> Frames,
    IReadOnlyList<GameDebugDumpFrameBlocks>? BlockFrames = null);

public sealed record GameDebugDumpFrameBlocks(
    int Index,
    IReadOnlyList<GameDebugDumpWorldBlocks> Worlds);

public sealed record GameDebugDumpWorldBlocks(
    string Name,
    IReadOnlyList<GameDebugDumpSceneBlocks> Scenes);

public sealed record GameDebugDumpSceneBlocks(
    string Name,
    IReadOnlyList<GameDebugDumpComponentStoreBlock> ComponentStores);

public sealed record GameDebugDumpComponentStoreBlock(
    DebugTypeInfo Type,
    int[] EntityIds,
    string Format,
    byte[] Payload,
    string? Error);

public sealed record GameDebugDumpRecordingRequest(
    string Command,
    string? Name = null,
    string? DumpDirectory = null);

public sealed record GameDebugDumpRecordingState(
    bool IsRecording,
    DateTimeOffset? StartedAt,
    int FrameCount,
    int DroppedFrameCount,
    string? LastDumpName,
    string? LastDumpPath);

public sealed record GameDebugDumpRecordingResult(
    bool Succeeded,
    string Message,
    GameDebugDumpRecordingState State);

public sealed record GameDebugDumpPlaybackOpenRequest(
    string? Path = null);

public sealed record GameDebugDumpPlaybackManifest(
    string Id,
    string Format,
    int Version,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DroppedFrameCount,
    IReadOnlyList<GameDebugDumpPlaybackFrame> Frames);

public sealed record GameDebugDumpPlaybackFrame(
    int Index,
    GameDebugFrameInfo Frame);

public sealed record GameDebugDumpPlaybackComponentRequest(
    string? PlaybackId,
    int FrameIndex,
    string WorldName,
    string SceneName,
    int EntityId,
    string ComponentTypeFullName,
    string? ComponentAssemblyName = null);
