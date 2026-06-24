using System.Globalization;
using System.Text.Json;
using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugDumpRecorder : IDisposable
{
    private const string DumpFormat = "nkg.debug.dump";
    private const int DumpVersion = 1;

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
        GameDebugOptions options)
    {
        _snapshots = snapshots;
        _control = control;
        _frames = frames;
        _options = options;
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
                now,
                NormalizeMaxFrames(_options.DumpMaxFrames));
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

        var lastFrame = recording.LastFrameNumber;
        CaptureFrame(
            recording,
            new GameDebugFrameInfo(
                recording.FrameCount,
                "recording-stop",
                lastFrame,
                DateTimeOffset.UtcNow),
            allowDetached: true);

        var endedAt = DateTimeOffset.UtcNow;
        var frames = recording.SnapshotFrames();
        var dump = new GameDebugDumpDocument(
            DumpFormat,
            DumpVersion,
            recording.Name,
            endedAt,
            recording.StartedAt,
            endedAt,
            recording.MaxFrames,
            recording.DroppedFrameCount,
            frames);
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
            _snapshots.Capture(CreateDumpCaptureOptions()),
            _control.GetState());

        lock (_gate)
        {
            if (ReferenceEquals(_recording, recording) || allowDetached)
            {
                recording.AddFrame(message);
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
                recording.FrameCount,
                recording.MaxFrames,
                recording.DroppedFrameCount,
                _lastDumpName,
                _lastDumpPath)
            : new GameDebugDumpRecordingState(
                false,
                null,
                0,
                NormalizeMaxFrames(_options.DumpMaxFrames),
                0,
                _lastDumpName,
                _lastDumpPath);
    }

    private GameDebugSnapshotCaptureOptions CreateDumpCaptureOptions()
    {
        return new GameDebugSnapshotCaptureOptions
        {
            IncludeComponentPayloads = _options.DumpIncludeComponentPayloads,
            IncludeStructuredComponentValues = _options.DumpIncludeStructuredComponentValues,
        };
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

    private static int NormalizeMaxFrames(int value)
    {
        return Math.Max(1, value);
    }

    private sealed class ActiveDumpRecording
    {
        private readonly Queue<GameDebugSnapshotMessage> _frames = [];

        public ActiveDumpRecording(string name, DateTimeOffset startedAt, int maxFrames)
        {
            Name = name;
            StartedAt = startedAt;
            MaxFrames = maxFrames;
        }

        public string Name { get; }

        public DateTimeOffset StartedAt { get; }

        public int MaxFrames { get; }

        public int FrameCount => _frames.Count;

        public int DroppedFrameCount { get; private set; }

        public long LastFrameNumber => _frames.Count > 0 ? _frames.Last().Frame.Frame : 0;

        public void AddFrame(GameDebugSnapshotMessage message)
        {
            while (_frames.Count >= MaxFrames)
            {
                _frames.Dequeue();
                DroppedFrameCount++;
            }

            _frames.Enqueue(message);
        }

        public IReadOnlyList<GameDebugSnapshotMessage> SnapshotFrames()
        {
            return _frames.ToArray();
        }
    }
}
