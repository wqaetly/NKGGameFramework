using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed record GameDebugDumpDocument(
    string Format,
    int Version,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DroppedFrameCount,
    IReadOnlyList<GameDebugSnapshotMessage> Frames);

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
