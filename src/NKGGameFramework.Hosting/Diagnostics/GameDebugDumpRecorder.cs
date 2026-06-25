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
            "start" => Start(request.Name, request.DumpDirectory),
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

    private GameDebugDumpRecordingResult Start(string? name, string? dumpDirectory)
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
                ResolveDumpDirectory(dumpDirectory, _options.DumpDirectory),
                now);
            _recording = recording;
            _frames.FramePublished += OnFramePublished;
        }

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

        var endedAt = DateTimeOffset.UtcNow;
        var frames = recording.SnapshotFrames();
        var dump = new GameDebugDumpDocument(
            DumpFormat,
            DumpVersion,
            recording.Name,
            endedAt,
            recording.StartedAt,
            endedAt,
            recording.DroppedFrameCount,
            frames);
        var path = SaveDump(dump, recording.DumpDirectory);

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
        GameDebugFrameInfo frame)
    {
        var message = new GameDebugSnapshotMessage(
            frame,
            _snapshots.Capture(CreateDumpCaptureOptions()),
            _control.GetState());

        lock (_gate)
        {
            if (ReferenceEquals(_recording, recording))
            {
                recording.AddFrame(message);
            }
        }
    }

    private string SaveDump(GameDebugDumpDocument dump, string dumpDirectory)
    {
        Directory.CreateDirectory(dumpDirectory);
        var fileName = $"{SanitizeFileName(dump.Name)}.nkgdump.json";
        var path = Path.Combine(dumpDirectory, fileName);
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
                recording.DroppedFrameCount,
                _lastDumpName,
                _lastDumpPath)
            : new GameDebugDumpRecordingState(
                false,
                null,
                0,
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

    private static string ResolveDumpDirectory(string? requestedDirectory, string fallbackDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(requestedDirectory)
            ? fallbackDirectory
            : requestedDirectory.Trim();

        return Path.GetFullPath(directory);
    }

    private sealed class ActiveDumpRecording
    {
        private readonly Queue<GameDebugSnapshotMessage> _frames = [];

        public ActiveDumpRecording(
            string name,
            string dumpDirectory,
            DateTimeOffset startedAt)
        {
            Name = name;
            DumpDirectory = dumpDirectory;
            StartedAt = startedAt;
        }

        public string Name { get; }

        public string DumpDirectory { get; }

        public DateTimeOffset StartedAt { get; }

        public int FrameCount => _frames.Count;

        public int DroppedFrameCount { get; private set; }

        public void AddFrame(GameDebugSnapshotMessage message)
        {
            _frames.Enqueue(message);
        }

        public IReadOnlyList<GameDebugSnapshotMessage> SnapshotFrames()
        {
            return _frames.ToArray();
        }
    }
}
