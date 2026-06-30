using System.Globalization;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Diagnostics;

public sealed class GameDebugDumpRecorder : IDisposable
{
    private const string DumpFormat = "nkg.debug.dump";
    private const int DumpDocumentVersion = 2;
    private const int MaxCachedPlaybackBlocks = 32;

    private readonly object _gate = new();
    private readonly IGameDebugSnapshotProvider _snapshots;
    private readonly GameDebugController _control;
    private readonly GameDebugFramePublisher _frames;
    private readonly GameDebugOptions _options;
    private readonly GameDebugSession? _session;
    private readonly object? _debugStateGate;
    private ActiveDumpRecording? _recording;
    private ActiveDumpPlayback? _playback;
    private string? _lastDumpName;
    private string? _lastDumpPath;
    private bool _disposed;

    public GameDebugDumpRecorder(
        IGameDebugSnapshotProvider snapshots,
        GameDebugController control,
        GameDebugFramePublisher frames,
        GameDebugOptions options,
        GameDebugSession? session = null,
        object? debugStateGate = null)
    {
        _snapshots = snapshots;
        _control = control;
        _frames = frames;
        _options = options;
        _session = session;
        _debugStateGate = debugStateGate;
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
                now,
                NormalizeMaxRecordedFrames(_options.MaxRecordedDumpFrames));
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
        var recordedFrames = recording.SnapshotFrames();
        var frames = recordedFrames
            .Select(static frame => frame.Message)
            .ToArray();
        var blockFrames = recordedFrames.Any(static frame => frame.Blocks is not null)
            ? recordedFrames
                .Select(static frame => frame.Blocks)
                .OfType<GameDebugDumpFrameBlocks>()
                .ToArray()
            : null;
        var dump = new GameDebugDumpDocument(
            DumpFormat,
            DumpDocumentVersion,
            recording.Name,
            endedAt,
            recording.StartedAt,
            endedAt,
            recording.DroppedFrameCount,
            frames,
            blockFrames);
        var path = SaveDump(dump, recording.DumpDirectory);

        lock (_gate)
        {
            _lastDumpName = dump.Name;
            _lastDumpPath = path;
        }

        return new GameDebugDumpRecordingResult(
            true,
            $"Debug dump recording saved to '{path}'.",
            GetState());
    }

    public GameDebugDumpPlaybackManifest OpenPlayback(GameDebugDumpPlaybackOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = string.IsNullOrWhiteSpace(request.Path)
            ? GetLastDumpPath()
            : Path.GetFullPath(request.Path.Trim());
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("No debug dump file has been recorded yet.");
        }

        var dump = GameDebugDumpFile.Deserialize(File.ReadAllBytes(path));
        return SetPlayback(dump, path);
    }

    public GameDebugDumpPlaybackManifest OpenPlayback(byte[] payload)
    {
        var dump = GameDebugDumpFile.Deserialize(payload);
        return SetPlayback(dump, null);
    }

    public GameDebugDumpAnalysisReport AnalyzeDump(GameDebugDumpPlaybackOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = string.IsNullOrWhiteSpace(request.Path)
            ? GetLastDumpPath()
            : Path.GetFullPath(request.Path.Trim());
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("No debug dump file has been recorded yet.");
        }

        return GameDebugDumpAnalyzer.Analyze(File.ReadAllBytes(path));
    }

    public GameDebugDumpAnalysisReport AnalyzeDump(byte[] payload)
    {
        return GameDebugDumpAnalyzer.Analyze(payload);
    }

    public GameDebugSnapshotMessage GetPlaybackFrame(string? playbackId, int frameIndex)
    {
        lock (_gate)
        {
            if (_playback is null)
            {
                throw new InvalidDataException("No debug dump playback is loaded.");
            }

            if (!string.IsNullOrWhiteSpace(playbackId) &&
                !StringComparer.Ordinal.Equals(_playback.Id, playbackId))
            {
                throw new InvalidDataException("The requested debug dump playback is no longer loaded.");
            }

            if (frameIndex < 0 || frameIndex >= _playback.Document.Frames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIndex), "The debug dump frame index was out of range.");
            }

            return _playback.Document.Frames[frameIndex];
        }
    }

    public ComponentDebugSnapshot GetPlaybackComponent(GameDebugDumpPlaybackComponentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ActiveDumpPlayback playback;
        lock (_gate)
        {
            playback = GetPlayback(request.PlaybackId);
        }

        var frame = GetPlaybackFrame(playback, request.FrameIndex);
        var world = frame.Snapshot.Worlds.SingleOrDefault(world =>
            StringComparer.Ordinal.Equals(world.Name, request.WorldName))
            ?? throw new InvalidDataException($"The debug dump world '{request.WorldName}' was not found.");
        var scene = world.Scenes.SingleOrDefault(scene =>
            StringComparer.Ordinal.Equals(scene.Name, request.SceneName))
            ?? throw new InvalidDataException($"The debug dump scene '{request.SceneName}' was not found.");
        var entity = scene.Entities.SingleOrDefault(entity => entity.Id == request.EntityId)
            ?? throw new InvalidDataException($"The debug dump entity '{request.EntityId}' was not found.");
        var component = entity.Components.SingleOrDefault(component =>
            StringComparer.Ordinal.Equals(component.Type.FullName, request.ComponentTypeFullName)
            && (string.IsNullOrWhiteSpace(request.ComponentAssemblyName)
                || StringComparer.Ordinal.Equals(component.Type.AssemblyName, request.ComponentAssemblyName)))
            ?? throw new InvalidDataException($"The debug dump component '{request.ComponentTypeFullName}' was not found.");

        if (TryFindBlock(
                playback.Document,
                request.FrameIndex,
                request.WorldName,
                request.SceneName,
                request.ComponentTypeFullName,
                request.ComponentAssemblyName,
                out var block))
        {
            return MaterializeBlockComponent(playback, component, block, request);
        }

        return GameDebugComponentValueMaterializer.MaterializeStructured(
            component,
            new OdinGameDebugComponentValueSerializer(),
            new GameDebugStructuredComponentValueCaptureOptions
            {
                MaxCollectionItems = 64,
                CaptureElementTemplate = false,
            });
    }

    private string? GetLastDumpPath()
    {
        lock (_gate)
        {
            return _lastDumpPath;
        }
    }

    private GameDebugDumpPlaybackManifest SetPlayback(GameDebugDumpDocument dump, string? path)
    {
        var playback = new ActiveDumpPlayback(Guid.NewGuid().ToString("N"), dump, path);
        lock (_gate)
        {
            _playback = playback;
        }

        return CreatePlaybackManifest(playback);
    }

    private static GameDebugDumpPlaybackManifest CreatePlaybackManifest(ActiveDumpPlayback playback)
    {
        var dump = playback.Document;
        return new GameDebugDumpPlaybackManifest(
            playback.Id,
            dump.Format,
            dump.Version,
            dump.Name,
            dump.CreatedAt,
            dump.StartedAt,
            dump.EndedAt,
            dump.DroppedFrameCount,
            dump.Frames
                .Select((message, index) => new GameDebugDumpPlaybackFrame(index, message.Frame))
                .ToArray());
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
        var captured = _session is null
            ? CaptureSnapshotFrame(frame)
            : CaptureBlockFrame(frame);

        lock (_gate)
        {
            if (ReferenceEquals(_recording, recording))
            {
                recording.AddFrame(captured);
            }
        }
    }

    private GameDebugDumpRecordedFrame CaptureSnapshotFrame(GameDebugFrameInfo frame)
    {
        return new GameDebugDumpRecordedFrame(
            new GameDebugSnapshotMessage(
                frame,
                _snapshots.Capture(CreateDumpCaptureOptions()),
                _control.GetState()),
            Blocks: null);
    }

    private GameDebugDumpRecordedFrame CaptureBlockFrame(GameDebugFrameInfo frame)
    {
        if (_debugStateGate is null)
        {
            return CaptureBlockFrameUnsafe(frame);
        }

        lock (_debugStateGate)
        {
            return CaptureBlockFrameUnsafe(frame);
        }
    }

    private GameDebugDumpRecordedFrame CaptureBlockFrameUnsafe(GameDebugFrameInfo frame)
    {
        var message = new GameDebugSnapshotMessage(
            frame,
            _snapshots.Capture(CreateDumpCaptureOptions()),
            _control.GetState());
        var blocks = new GameDebugDumpFrameBlocks(
            0,
            _session!.GetWorlds()
                .Select(CaptureWorldBlocks)
                .ToArray());

        return new GameDebugDumpRecordedFrame(message, blocks);
    }

    private static GameDebugDumpWorldBlocks CaptureWorldBlocks(World world)
    {
        return new GameDebugDumpWorldBlocks(
            world.Name,
            world.Scenes
                .Select(static scene => new GameDebugDumpSceneBlocks(
                    scene.Name,
                    scene.ComponentStoreDumpBlocks
                        .Select(GameDebugComponentStoreBlockSerializer.Serialize)
                        .ToArray()))
                .ToArray());
    }

    private string SaveDump(GameDebugDumpDocument dump, string dumpDirectory)
    {
        Directory.CreateDirectory(dumpDirectory);
        var fileName = $"{SanitizeFileName(dump.Name)}{GameDebugDumpFile.FileExtension}";
        var path = Path.Combine(dumpDirectory, fileName);
        File.WriteAllBytes(path, GameDebugDumpFile.Serialize(dump));
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
            IncludeComponentPayloads = false,
            IncludeStructuredComponentValues = false,
        };
    }

    private ActiveDumpPlayback GetPlayback(string? playbackId)
    {
        if (_playback is null)
        {
            throw new InvalidDataException("No debug dump playback is loaded.");
        }

        if (!string.IsNullOrWhiteSpace(playbackId) &&
            !StringComparer.Ordinal.Equals(_playback.Id, playbackId))
        {
            throw new InvalidDataException("The requested debug dump playback is no longer loaded.");
        }

        return _playback;
    }

    private static GameDebugSnapshotMessage GetPlaybackFrame(ActiveDumpPlayback playback, int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= playback.Document.Frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex), "The debug dump frame index was out of range.");
        }

        return playback.Document.Frames[frameIndex];
    }

    private static bool TryFindBlock(
        GameDebugDumpDocument dump,
        int frameIndex,
        string worldName,
        string sceneName,
        string componentTypeFullName,
        string? componentAssemblyName,
        out GameDebugDumpComponentStoreBlock block)
    {
        if (dump.BlockFrames is not { Count: > 0 } blockFrames)
        {
            block = null!;
            return false;
        }

        var frameBlocks = blockFrames.SingleOrDefault(frame => frame.Index == frameIndex);
        if (frameBlocks is null)
        {
            block = null!;
            return false;
        }

        var world = frameBlocks.Worlds.SingleOrDefault(world =>
            StringComparer.Ordinal.Equals(world.Name, worldName));
        var scene = world?.Scenes.SingleOrDefault(scene =>
            StringComparer.Ordinal.Equals(scene.Name, sceneName));
        var found = scene?.ComponentStores.SingleOrDefault(store =>
            StringComparer.Ordinal.Equals(store.Type.FullName, componentTypeFullName)
            && (string.IsNullOrWhiteSpace(componentAssemblyName)
                || StringComparer.Ordinal.Equals(store.Type.AssemblyName, componentAssemblyName)));

        block = found!;
        return found is not null;
    }

    private static ComponentDebugSnapshot MaterializeBlockComponent(
        ActiveDumpPlayback playback,
        ComponentDebugSnapshot component,
        GameDebugDumpComponentStoreBlock block,
        GameDebugDumpPlaybackComponentRequest request)
    {
        if (StringComparer.Ordinal.Equals(block.Format, GameDebugComponentStoreBlockSerializer.StructuredFormat))
        {
            return GameDebugComponentStoreBlockSerializer.TryGetStructuredValue(
                block,
                request.EntityId,
                out var structuredValue,
                out var structuredError)
                ? component with
                {
                    Value = structuredValue,
                }
                : component with
                {
                    Value = new ComponentValueDebugSnapshot(
                        block.Format,
                        Payload: null,
                        structuredError),
                };
        }

        var row = Array.IndexOf(block.EntityIds, request.EntityId);
        if (row < 0)
        {
            return component with
            {
                Value = new ComponentValueDebugSnapshot(
                    GameDebugComponentStoreBlockSerializer.Format,
                    Payload: null,
                    $"Entity {request.EntityId} was not present in component store '{block.Type.Name}'."),
            };
        }

        var cacheKey = CreatePlaybackBlockCacheKey(request, block);
        if (!playback.TryGetBlockValues(cacheKey, block, out var values, out var error))
        {
            return component with
            {
                Value = new ComponentValueDebugSnapshot(
                    GameDebugComponentStoreBlockSerializer.Format,
                    Payload: null,
                    error),
            };
        }

        object value;
        try
        {
            value = values.GetValue(row)
                ?? throw new InvalidDataException("The component store block contained a null row.");
        }
        catch (Exception exception)
        {
            return component with
            {
                Value = new ComponentValueDebugSnapshot(
                    GameDebugComponentStoreBlockSerializer.Format,
                    Payload: null,
                    exception.Message),
            };
        }

        var serializer = new OdinGameDebugComponentValueSerializer();
        return component with
        {
            Value = serializer.Serialize(
                value,
                new GameDebugComponentValueSerializationOptions
                {
                    IncludePayload = false,
                    IncludeStructured = true,
                    StructuredCaptureOptions = new GameDebugStructuredComponentValueCaptureOptions
                    {
                        MaxCollectionItems = 64,
                        CaptureElementTemplate = false,
                    },
                }),
        };
    }

    private static string CreatePlaybackBlockCacheKey(
        GameDebugDumpPlaybackComponentRequest request,
        GameDebugDumpComponentStoreBlock block)
    {
        return string.Join(
            '\0',
            request.FrameIndex.ToString(CultureInfo.InvariantCulture),
            request.WorldName,
            request.SceneName,
            block.Type.AssemblyName,
            block.Type.FullName);
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

    private static int? NormalizeMaxRecordedFrames(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(GameDebugOptions.MaxRecordedDumpFrames),
                "The maximum recorded debug dump frame count must be positive.");
    }

    private sealed class ActiveDumpRecording
    {
        private readonly Queue<GameDebugDumpRecordedFrame> _frames = [];
        private readonly int? _maxFrames;

        public ActiveDumpRecording(
            string name,
            string dumpDirectory,
            DateTimeOffset startedAt,
            int? maxFrames)
        {
            Name = name;
            DumpDirectory = dumpDirectory;
            StartedAt = startedAt;
            _maxFrames = maxFrames;
        }

        public string Name { get; }

        public string DumpDirectory { get; }

        public DateTimeOffset StartedAt { get; }

        public int FrameCount => _frames.Count;

        public int DroppedFrameCount { get; private set; }

        public void AddFrame(GameDebugDumpRecordedFrame frame)
        {
            _frames.Enqueue(frame);
            if (_maxFrames is not { } maxFrames)
            {
                return;
            }

            while (_frames.Count > maxFrames)
            {
                _frames.Dequeue();
                DroppedFrameCount++;
            }
        }

        public IReadOnlyList<GameDebugDumpRecordedFrame> SnapshotFrames()
        {
            return _frames
                .Select((frame, index) => frame.Blocks is null
                    ? frame
                    : frame with
                    {
                        Blocks = frame.Blocks with
                        {
                            Index = index,
                        },
                    })
                .ToArray();
        }
    }

    private sealed record GameDebugDumpRecordedFrame(
        GameDebugSnapshotMessage Message,
        GameDebugDumpFrameBlocks? Blocks);

    private sealed class ActiveDumpPlayback
    {
        private readonly Dictionary<string, Array> _blockValues = new(StringComparer.Ordinal);
        private readonly Queue<string> _blockValueKeys = [];

        public ActiveDumpPlayback(
            string id,
            GameDebugDumpDocument document,
            string? path)
        {
            Id = id;
            Document = document;
            Path = path;
        }

        public string Id { get; }

        public GameDebugDumpDocument Document { get; }

        public string? Path { get; }

        public bool TryGetBlockValues(
            string cacheKey,
            GameDebugDumpComponentStoreBlock block,
            out Array values,
            out string? error)
        {
            if (_blockValues.TryGetValue(cacheKey, out values!))
            {
                error = null;
                return true;
            }

            try
            {
                values = GameDebugComponentStoreBlockSerializer.DeserializeValues(block);
                AddBlockValues(cacheKey, values);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                values = null!;
                error = exception.Message;
                return false;
            }
        }

        private void AddBlockValues(string cacheKey, Array values)
        {
            if (_blockValues.ContainsKey(cacheKey))
            {
                return;
            }

            _blockValues.Add(cacheKey, values);
            _blockValueKeys.Enqueue(cacheKey);

            while (_blockValues.Count > MaxCachedPlaybackBlocks)
            {
                var oldest = _blockValueKeys.Dequeue();
                _blockValues.Remove(oldest);
            }
        }
    }
}
