using System.Globalization;
using System.Text.Json;

namespace NKGGameFramework.Diagnostics;

public sealed class GameDebugEndpointDispatcher : IDisposable
{
    private readonly string _endpointPrefix;
    private readonly bool _defaultWaitForSnapshotFrame;
    private readonly object _debugStateGate;
    private readonly GameDebugController _control;
    private readonly GameDebugFramePublisher _frames;
    private readonly IGameDebugSnapshotProvider _snapshots;
    private readonly IGameDebugMutationHandler _mutations;
    private readonly GameDebugDumpRecorder _dumps;
    private readonly bool _ownsDumps;
    private bool _disposed;

    public GameDebugEndpointDispatcher(GameDebugEndpointDispatcherOptions? options = null)
    {
        options ??= new GameDebugEndpointDispatcherOptions();
        _endpointPrefix = NormalizeEndpointPrefix(options.EndpointPrefix);
        _defaultWaitForSnapshotFrame = options.DefaultWaitForSnapshotFrame;
        _debugStateGate = options.DebugStateGate ?? new object();
        _control = options.Control ?? GameDebugController.Shared;
        _frames = options.Frames ?? GameDebugFramePublisher.Shared;

        var session = options.Session ?? new GameDebugSession();
        var debugOptions = new GameDebugOptions
        {
            EnableMutations = options.EnableMutations,
        };
        if (!string.IsNullOrWhiteSpace(options.DumpDirectory))
        {
            debugOptions.DumpDirectory = options.DumpDirectory;
        }
        debugOptions.MaxRecordedDumpFrames = options.MaxRecordedDumpFrames;
        debugOptions.DumpRecordingFrameStride = options.DumpRecordingFrameStride;

        var serializer = options.ComponentValueSerializer ?? new OdinGameDebugComponentValueSerializer();
        _snapshots = options.Snapshots ?? new GameDebugSnapshotProvider(session, serializer);
        _mutations = options.Mutations ?? new GameDebugMutationHandler(session, debugOptions, serializer);
        if (options.Dumps is { } dumps)
        {
            _dumps = dumps;
            _ownsDumps = false;
        }
        else
        {
            _dumps = new GameDebugDumpRecorder(
                _snapshots,
                _control,
                _frames,
                debugOptions,
                session,
                _debugStateGate);
            _ownsDumps = true;
        }
    }

    public async ValueTask<GameDebugEndpointResponse> HandleAsync(
        GameDebugEndpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException exception)
        {
            return LeanJsonResponse(400, "Bad Request", new DebugErrorResponse(FormatException(exception)));
        }
        catch (InvalidDataException exception)
        {
            return LeanJsonResponse(400, "Bad Request", new DebugErrorResponse(FormatException(exception)));
        }
        catch (ArgumentException exception)
        {
            return LeanJsonResponse(400, "Bad Request", new DebugErrorResponse(FormatException(exception)));
        }
        catch (Exception exception)
        {
            return LeanJsonResponse(500, "Internal Server Error", new DebugErrorResponse(FormatException(exception)));
        }
    }

    public GameDebugEndpointResponse Handle(GameDebugEndpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return Dispatch(request);
        }
        catch (JsonException exception)
        {
            return LeanJsonResponse(400, "Bad Request", new DebugErrorResponse(FormatException(exception)));
        }
        catch (InvalidDataException exception)
        {
            return LeanJsonResponse(400, "Bad Request", new DebugErrorResponse(FormatException(exception)));
        }
        catch (ArgumentException exception)
        {
            return LeanJsonResponse(400, "Bad Request", new DebugErrorResponse(FormatException(exception)));
        }
        catch (Exception exception)
        {
            return LeanJsonResponse(500, "Internal Server Error", new DebugErrorResponse(FormatException(exception)));
        }
    }

    public GameDebugSnapshotMessage CaptureSnapshotMessage(
        GameDebugFrameInfo frame,
        GameDebugSnapshotCaptureOptions captureOptions)
    {
        return ExecuteDebugOperation(() => new GameDebugSnapshotMessage(
            frame,
            _snapshots.Capture(captureOptions),
            _control.GetState()));
    }

    public GameDebugSnapshotMessage CaptureCurrentSnapshotMessage(
        GameDebugSnapshotCaptureOptions captureOptions)
    {
        return CaptureSnapshotMessage(
            _frames.GetLastPublished() ?? new GameDebugFrameInfo(
                0,
                "Snapshot",
                0,
                DateTimeOffset.UtcNow),
            captureOptions);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsDumps)
        {
            _dumps.Dispose();
        }
    }

    private async ValueTask<GameDebugEndpointResponse> DispatchAsync(
        GameDebugEndpointRequest request,
        CancellationToken cancellationToken)
    {
        var target = ParseTarget(request.Target);
        if (StringComparer.OrdinalIgnoreCase.Equals(request.Method, "OPTIONS"))
        {
            return NoContentResponse();
        }

        if (!TryGetEndpoint(target.Path, out var endpoint))
        {
            return LeanJsonResponse(404, "Not Found", new DebugErrorResponse("Debug endpoint was not found."));
        }

        if (IsGet(request, endpoint, "/health"))
        {
            return LeanJsonResponse(200, "OK", new DebugHealthResponse("ok", DateTimeOffset.UtcNow));
        }

        if (IsGet(request, endpoint, "/snapshot") || IsGet(request, endpoint, "/stream"))
        {
            var captureOptions = CreateSnapshotCaptureOptions(target.Query);
            if (StringComparer.Ordinal.Equals(endpoint, "/stream"))
            {
                captureOptions = CreateStreamSnapshotCaptureOptions(captureOptions);
            }

            var waitForFrame = GetBool(target.Query, "waitForFrame") ?? _defaultWaitForSnapshotFrame;
            var message = waitForFrame
                ? await CaptureSnapshotOnNextFrameAsync(captureOptions, cancellationToken).ConfigureAwait(false)
                : CaptureCurrentSnapshotMessage(captureOptions);
            return JsonResponse(200, "OK", message);
        }

        if (IsGet(request, endpoint, "/control"))
        {
            return JsonResponse(200, "OK", ExecuteDebugOperation(_control.GetState));
        }

        if (IsPost(request, endpoint, "/control"))
        {
            var body = ReadJsonBody<GameDebugControlRequest>(request.Body);
            return JsonResponse(200, "OK", ExecuteDebugOperation(() => _control.Execute(body)));
        }

        if (IsPost(request, endpoint, "/mutations"))
        {
            var body = ReadJsonBody<GameDebugMutationRequest>(request.Body);
            return JsonResponse(200, "OK", ExecuteDebugOperation(() => ExecutePausedMutation(body)));
        }

        if (IsGet(request, endpoint, "/dump/recording"))
        {
            return JsonResponse(200, "OK", ExecuteDebugOperation(_dumps.GetState));
        }

        if (IsPost(request, endpoint, "/dump/recording"))
        {
            var body = ReadJsonBody<GameDebugDumpRecordingRequest>(request.Body);
            return JsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.Execute(body)));
        }

        if (IsPost(request, endpoint, "/dump/playback"))
        {
            var body = ReadJsonBody<GameDebugDumpPlaybackOpenRequest>(request.Body);
            return JsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.OpenPlayback(body)));
        }

        if (IsPost(request, endpoint, "/dump/playback/upload"))
        {
            return JsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.OpenPlayback(request.Body)));
        }

        if (IsPost(request, endpoint, "/dump/analysis"))
        {
            var body = ReadJsonBody<GameDebugDumpPlaybackOpenRequest>(request.Body);
            return JsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.AnalyzeDump(body)));
        }

        if (IsPost(request, endpoint, "/dump/analysis/upload"))
        {
            return JsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.AnalyzeDump(request.Body)));
        }

        if (IsGet(request, endpoint, "/dump/playback/frame"))
        {
            var frameIndex = GetInt(target.Query, "frameIndex") ?? GetInt(target.Query, "index") ?? 0;
            return JsonResponse(
                200,
                "OK",
                ExecuteDebugOperation(() => _dumps.GetPlaybackFrame(GetString(target.Query, "playbackId"), frameIndex)));
        }

        if (IsGet(request, endpoint, "/dump/playback/component"))
        {
            var body = new GameDebugDumpPlaybackComponentRequest(
                GetString(target.Query, "playbackId"),
                GetInt(target.Query, "frameIndex") ?? GetInt(target.Query, "index") ?? 0,
                GetString(target.Query, "worldName") ?? GetString(target.Query, "world") ?? string.Empty,
                GetString(target.Query, "sceneName") ?? GetString(target.Query, "scene") ?? string.Empty,
                GetInt(target.Query, "entityId") ?? 0,
                GetString(target.Query, "componentTypeFullName") ?? GetString(target.Query, "component") ?? string.Empty,
                GetString(target.Query, "componentAssemblyName") ?? GetString(target.Query, "componentAssembly"));
            return JsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.GetPlaybackComponent(body)));
        }

        return JsonResponse(
            405,
            "Method Not Allowed",
            new DebugErrorResponse("Debug endpoint does not support this method."));
    }

    private GameDebugEndpointResponse Dispatch(GameDebugEndpointRequest request)
    {
        var target = ParseTarget(request.Target);
        if (StringComparer.OrdinalIgnoreCase.Equals(request.Method, "OPTIONS"))
        {
            return NoContentResponse();
        }

        if (!TryGetEndpoint(target.Path, out var endpoint))
        {
            return LeanJsonResponse(404, "Not Found", new DebugErrorResponse("Debug endpoint was not found."));
        }

        if (IsGet(request, endpoint, "/health"))
        {
            return LeanJsonResponse(200, "OK", new DebugHealthResponse("ok", DateTimeOffset.UtcNow));
        }

        if (IsGet(request, endpoint, "/snapshot") || IsGet(request, endpoint, "/stream"))
        {
            var captureOptions = CreateSnapshotCaptureOptions(target.Query);
            if (StringComparer.Ordinal.Equals(endpoint, "/stream"))
            {
                captureOptions = CreateStreamSnapshotCaptureOptions(captureOptions);
            }

            return LeanJsonResponse(200, "OK", CaptureCurrentSnapshotMessage(captureOptions));
        }

        if (IsGet(request, endpoint, "/control"))
        {
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(_control.GetState));
        }

        if (IsPost(request, endpoint, "/control"))
        {
            var body = GameDebugEndpointLeanJson.DeserializeControlRequest(request.Body);
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(() => _control.Execute(body)));
        }

        if (IsPost(request, endpoint, "/mutations"))
        {
            var body = GameDebugEndpointLeanJson.DeserializeMutationRequest(request.Body);
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(() => ExecutePausedMutation(body)));
        }

        if (IsGet(request, endpoint, "/dump/recording"))
        {
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(_dumps.GetState));
        }

        if (IsPost(request, endpoint, "/dump/recording"))
        {
            var body = GameDebugEndpointLeanJson.DeserializeRecordingRequest(request.Body);
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.Execute(body)));
        }

        if (IsPost(request, endpoint, "/dump/playback"))
        {
            var body = GameDebugEndpointLeanJson.DeserializePlaybackOpenRequest(request.Body);
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.OpenPlayback(body)));
        }

        if (IsPost(request, endpoint, "/dump/playback/upload"))
        {
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.OpenPlayback(request.Body)));
        }

        if (IsPost(request, endpoint, "/dump/analysis"))
        {
            var body = GameDebugEndpointLeanJson.DeserializePlaybackOpenRequest(request.Body);
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.AnalyzeDump(body)));
        }

        if (IsPost(request, endpoint, "/dump/analysis/upload"))
        {
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.AnalyzeDump(request.Body)));
        }

        if (IsGet(request, endpoint, "/dump/playback/frame"))
        {
            var frameIndex = GetInt(target.Query, "frameIndex") ?? GetInt(target.Query, "index") ?? 0;
            return LeanJsonResponse(
                200,
                "OK",
                ExecuteDebugOperation(() => _dumps.GetPlaybackFrame(GetString(target.Query, "playbackId"), frameIndex)));
        }

        if (IsGet(request, endpoint, "/dump/playback/component"))
        {
            var body = new GameDebugDumpPlaybackComponentRequest(
                GetString(target.Query, "playbackId"),
                GetInt(target.Query, "frameIndex") ?? GetInt(target.Query, "index") ?? 0,
                GetString(target.Query, "worldName") ?? GetString(target.Query, "world") ?? string.Empty,
                GetString(target.Query, "sceneName") ?? GetString(target.Query, "scene") ?? string.Empty,
                GetInt(target.Query, "entityId") ?? 0,
                GetString(target.Query, "componentTypeFullName") ?? GetString(target.Query, "component") ?? string.Empty,
                GetString(target.Query, "componentAssemblyName") ?? GetString(target.Query, "componentAssembly"));
            return LeanJsonResponse(200, "OK", ExecuteDebugOperation(() => _dumps.GetPlaybackComponent(body)));
        }

        return LeanJsonResponse(
            405,
            "Method Not Allowed",
            new DebugErrorResponse("Debug endpoint does not support this method."));
    }

    private async ValueTask<GameDebugSnapshotMessage> CaptureSnapshotOnNextFrameAsync(
        GameDebugSnapshotCaptureOptions captureOptions,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<GameDebugSnapshotMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFramePublished(GameDebugFrameInfo info)
        {
            _frames.FramePublished -= OnFramePublished;
            try
            {
                completion.TrySetResult(CaptureSnapshotMessage(info, captureOptions));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        _frames.FramePublished += OnFramePublished;
        using var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource<GameDebugSnapshotMessage>)state!).TrySetCanceled(),
            completion);
        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _frames.FramePublished -= OnFramePublished;
        }
    }

    private GameDebugMutationResult ExecutePausedMutation(GameDebugMutationRequest request)
    {
        var state = _control.GetState();
        return state is { IsPaused: true, PendingStepCount: 0 }
            ? _mutations.Execute(request)
            : new GameDebugMutationResult(false, "Pause debug playback before editing components.");
    }

    private T ExecuteDebugOperation<T>(Func<T> operation)
    {
        lock (_debugStateGate)
        {
            return operation();
        }
    }

    public static GameDebugSnapshotCaptureOptions CreateSnapshotCaptureOptions(string target)
    {
        return CreateSnapshotCaptureOptions(ParseTarget(target).Query);
    }

    public static GameDebugSnapshotCaptureOptions CreateSnapshotCaptureOptions(
        IReadOnlyDictionary<string, string> query)
    {
        return new GameDebugSnapshotCaptureOptions
        {
            WorldName = GetString(query, "world") ?? GetString(query, "worldName"),
            SceneName = GetString(query, "scene") ?? GetString(query, "sceneName"),
            EntityId = GetInt(query, "entityId"),
            ComponentTypeFullName = GetString(query, "componentTypeFullName")
                ?? GetString(query, "component"),
            ComponentAssemblyName = GetString(query, "componentAssemblyName")
                ?? GetString(query, "componentAssembly"),
            EntityOffset = Math.Max(0, GetInt(query, "entityOffset") ?? GetInt(query, "offset") ?? 0),
            EntityLimit = NormalizeLimit(GetInt(query, "entityLimit") ?? GetInt(query, "limit")),
            IncludeComponentPayloads = GetBool(query, "includePayload")
                ?? GetBool(query, "includeComponentPayloads")
                ?? true,
            IncludeStructuredComponentValues = GetBool(query, "includeStructured")
                ?? GetBool(query, "includeStructuredComponentValues")
                ?? true,
        };
    }

    public static GameDebugSnapshotCaptureOptions CreateStreamSnapshotCaptureOptions(
        GameDebugSnapshotCaptureOptions captureOptions)
    {
        return captureOptions with
        {
            IncludeComponentPayloads = false,
            IncludeStructuredComponentValues = false,
        };
    }

    private bool TryGetEndpoint(string path, out string endpoint)
    {
        endpoint = string.Empty;
        if (!path.StartsWith(_endpointPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        endpoint = path[_endpointPrefix.Length..];
        if (endpoint.Length == 0)
        {
            endpoint = "/";
            return true;
        }

        return endpoint.StartsWith("/", StringComparison.Ordinal);
    }

    private static DebugHttpTarget ParseTarget(string rawTarget)
    {
        string path;
        string query;
        if (Uri.TryCreate(rawTarget, UriKind.Absolute, out var absolute))
        {
            path = absolute.AbsolutePath;
            query = absolute.Query;
        }
        else
        {
            var queryStart = rawTarget.IndexOf('?');
            path = queryStart >= 0 ? rawTarget[..queryStart] : rawTarget;
            query = queryStart >= 0 ? rawTarget[queryStart..] : string.Empty;
        }

        return new DebugHttpTarget(path, ParseQuery(query));
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = query[0] == '?' ? query[1..] : query;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            var value = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            values[DecodeQueryValue(key)] = DecodeQueryValue(value);
        }

        return values;
    }

    private static string DecodeQueryValue(string value)
    {
        return Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
    }

    private static T ReadJsonBody<T>(byte[] body)
    {
        if (body.Length == 0)
        {
            throw new JsonException("The debug request body was empty.");
        }

        return JsonSerializer.Deserialize<T>(body, GameDebugJson.Options)
            ?? throw new JsonException($"The debug request body could not be deserialized as {typeof(T).Name}.");
    }

    private static GameDebugEndpointResponse NoContentResponse()
    {
        return new GameDebugEndpointResponse(
            204,
            "No Content",
            "text/plain; charset=utf-8",
            []);
    }

    private static GameDebugEndpointResponse JsonResponse<T>(int statusCode, string reasonPhrase, T value)
    {
        return new GameDebugEndpointResponse(
            statusCode,
            reasonPhrase,
            "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(value, GameDebugJson.Options));
    }

    private static GameDebugEndpointResponse LeanJsonResponse<T>(int statusCode, string reasonPhrase, T value)
    {
        var body = value switch
        {
            DebugHealthResponse health => GameDebugEndpointLeanJson.SerializeHealth(health.CapturedAt),
            DebugErrorResponse error => GameDebugEndpointLeanJson.SerializeError(error.Message),
            GameDebugSnapshotMessage message => GameDebugEndpointLeanJson.Serialize(message),
            GameDebugControlState state => GameDebugEndpointLeanJson.Serialize(state),
            GameDebugControlResult result => GameDebugEndpointLeanJson.Serialize(result),
            GameDebugMutationResult result => GameDebugEndpointLeanJson.Serialize(result),
            GameDebugDumpRecordingState state => GameDebugEndpointLeanJson.Serialize(state),
            GameDebugDumpRecordingResult result => GameDebugEndpointLeanJson.Serialize(result),
            GameDebugDumpPlaybackManifest manifest => GameDebugEndpointLeanJson.Serialize(manifest),
            GameDebugDumpAnalysisReport report => GameDebugEndpointLeanJson.Serialize(report),
            ComponentDebugSnapshot component => GameDebugEndpointLeanJson.Serialize(component),
            _ => throw new NotSupportedException(
                $"Lean WebDebug JSON does not support {typeof(T).FullName}. Add an explicit GameDebugEndpointLeanJson serializer instead of falling back to System.Text.Json."),
        };

        return new GameDebugEndpointResponse(
            statusCode,
            reasonPhrase,
            "application/json; charset=utf-8",
            body);
    }

    private static string? GetString(IReadOnlyDictionary<string, string> query, string key)
    {
        if (!query.TryGetValue(key, out var value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? GetInt(IReadOnlyDictionary<string, string> query, string key)
    {
        return query.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : null;
    }

    private static int? NormalizeLimit(int? value)
    {
        return value is > 0 ? value : null;
    }

    private static string FormatException(Exception exception)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(exception.GetType().FullName ?? exception.GetType().Name)
            .Append(": ")
            .Append(exception.Message);
        if (exception.InnerException is { } inner)
        {
            builder.Append(" | inner: ")
                .Append(inner.GetType().FullName ?? inner.GetType().Name)
                .Append(": ")
                .Append(inner.Message);
        }

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            builder.Append(" | stack: ")
                .Append(exception.StackTrace);
        }

        return builder.ToString();
    }

    private static bool? GetBool(IReadOnlyDictionary<string, string> query, string key)
    {
        if (!query.TryGetValue(key, out var value))
        {
            return null;
        }

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };
    }

    private static bool IsGet(GameDebugEndpointRequest request, string endpoint, string expectedEndpoint)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(request.Method, "GET")
            && StringComparer.Ordinal.Equals(endpoint, expectedEndpoint);
    }

    private static bool IsPost(GameDebugEndpointRequest request, string endpoint, string expectedEndpoint)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(request.Method, "POST")
            && StringComparer.Ordinal.Equals(endpoint, expectedEndpoint);
    }

    private static string NormalizeEndpointPrefix(string prefix)
    {
        var normalized = prefix.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = "/" + normalized;
        }

        return normalized.Length > 1
            ? normalized.TrimEnd('/')
            : normalized;
    }

    private sealed record DebugHttpTarget(
        string Path,
        IReadOnlyDictionary<string, string> Query);

    private sealed record DebugHealthResponse(
        string Status,
        DateTimeOffset CapturedAt);

    private sealed record DebugErrorResponse(string Message);
}
