using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugDumpRecorder : IDisposable
{
    private const string DumpFormat = "nkg.debug.dump";
    private const int DumpVersion = 1;

    private static readonly GameDebugSnapshotCaptureOptions DumpCaptureOptions = new()
    {
        IncludeComponentPayloads = true,
        IncludeStructuredComponentValues = true,
    };

    private readonly object _gate = new();
    private readonly IGameDebugSnapshotProvider _snapshots;
    private readonly GameDebugController _control;
    private readonly GameDebugFramePublisher _frames;
    private readonly GameDebugOptions _options;
    private ActiveDumpRecording? _recording;
    private string? _lastDumpName;
    private string? _lastDumpPath;
    private bool _disposed;

    public GameDebugDumpRecorder(
        IGameDebugSnapshotProvider snapshots,
        GameDebugController control,
        GameDebugFramePublisher frames,
        IOptions<GameDebugOptions> options)
    {
        _snapshots = snapshots;
        _control = control;
        _frames = frames;
        _options = options.Value;
    }

    public GameDebugDumpRecordingState GetState()
    {
        lock (_gate)
        {
            return CreateState();
        }
    }

    public GameDebugDumpRecordingResult Execute(GameDebugDumpRecordingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Command.Trim().ToLowerInvariant() switch
        {
            "start" => Start(request.Name),
            "stop" or "end" => Stop(),
            _ => new GameDebugDumpRecordingResult(
                false,
                $"Unknown dump recording command '{request.Command}'.",
                GetState()),
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frames.FramePublished -= OnFramePublished;
    }

    private GameDebugDumpRecordingResult Start(string? name)
    {
        ActiveDumpRecording recording;
        lock (_gate)
        {
            if (_recording is not null)
            {
                return new GameDebugDumpRecordingResult(
                    false,
                    "A debug dump recording is already running.",
                    CreateState());
            }

            var now = DateTimeOffset.UtcNow;
            recording = new ActiveDumpRecording(
                NormalizeDumpName(name, now),
                now);
            _recording = recording;
            _frames.FramePublished += OnFramePublished;
        }

        CaptureFrame(
            recording,
            new GameDebugFrameInfo(0, "recording-start", 0, DateTimeOffset.UtcNow));

        return new GameDebugDumpRecordingResult(
            true,
            "Debug dump recording started.",
            GetState());
    }

    private GameDebugDumpRecordingResult Stop()
    {
        ActiveDumpRecording recording;
        lock (_gate)
        {
            if (_recording is null)
            {
                return new GameDebugDumpRecordingResult(
                    false,
                    "No debug dump recording is running.",
                    CreateState());
            }

            recording = _recording;
            _recording = null;
            _frames.FramePublished -= OnFramePublished;
        }

        var lastFrame = recording.Frames.Count > 0
            ? recording.Frames[^1].Frame.Frame
            : 0;
        CaptureFrame(
            recording,
            new GameDebugFrameInfo(
                recording.Frames.Count,
                "recording-stop",
                lastFrame,
                DateTimeOffset.UtcNow),
            allowDetached: true);

        var endedAt = DateTimeOffset.UtcNow;
        var dump = new GameDebugDumpDocument(
            DumpFormat,
            DumpVersion,
            recording.Name,
            endedAt,
            recording.StartedAt,
            endedAt,
            recording.Frames.ToArray());
        var path = SaveDump(dump);

        lock (_gate)
        {
            _lastDumpName = dump.Name;
            _lastDumpPath = path;
        }

        return new GameDebugDumpRecordingResult(
            true,
            $"Debug dump recording saved to '{path}'.",
            GetState(),
            dump);
    }

    private void OnFramePublished(GameDebugFrameInfo info)
    {
        ActiveDumpRecording? recording;
        lock (_gate)
        {
            recording = _recording;
        }

        if (recording is null)
        {
            return;
        }

        CaptureFrame(recording, info);
    }

    private void CaptureFrame(
        ActiveDumpRecording recording,
        GameDebugFrameInfo frame,
        bool allowDetached = false)
    {
        var message = new GameDebugSnapshotMessage(
            frame,
            _snapshots.Capture(DumpCaptureOptions),
            _control.GetState());

        lock (_gate)
        {
            if (ReferenceEquals(_recording, recording) || allowDetached)
            {
                recording.Frames.Add(message);
            }
        }
    }

    private string SaveDump(GameDebugDumpDocument dump)
    {
        Directory.CreateDirectory(_options.DumpDirectory);
        var fileName = $"{SanitizeFileName(dump.Name)}.nkgdump.json";
        var path = Path.Combine(_options.DumpDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(dump, GameDebugJson.Options));
        return path;
    }

    private GameDebugDumpRecordingState CreateState()
    {
        return _recording is { } recording
            ? new GameDebugDumpRecordingState(
                true,
                recording.StartedAt,
                recording.Frames.Count,
                _lastDumpName,
                _lastDumpPath)
            : new GameDebugDumpRecordingState(
                false,
                null,
                0,
                _lastDumpName,
                _lastDumpPath);
    }

    private static string NormalizeDumpName(string? name, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return $"nkg-debug-{now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "nkg-debug-dump" : sanitized;
    }

    private sealed class ActiveDumpRecording
    {
        public ActiveDumpRecording(string name, DateTimeOffset startedAt)
        {
            Name = name;
            StartedAt = startedAt;
        }

        public string Name { get; }

        public DateTimeOffset StartedAt { get; }

        public List<GameDebugSnapshotMessage> Frames { get; } = [];
    }
}
