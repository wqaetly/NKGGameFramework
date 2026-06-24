using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Cysharp.Threading.Tasks;
using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugHost : IAsyncDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 16 * 1024 * 1024;
    private static readonly byte[] HeaderTerminator = [13, 10, 13, 10];

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _connectionSlots;
    private readonly object _connectionGate = new();
    private readonly object _debugStateGate;
    private readonly UniTask _acceptLoop;
    private readonly string _endpointPrefix;
    private readonly GameDebugController _control;
    private readonly GameDebugFramePublisher _frames;
    private readonly IGameDebugSnapshotProvider _snapshots;
    private readonly IGameDebugMutationHandler _mutations;
    private readonly GameDebugDumpRecorder _dumps;
    private int _activeConnectionCount;
    private UniTaskCompletionSource? _connectionsDrained;
    private bool _disposed;

    private GameDebugHost(
        TcpListener listener,
        Uri baseAddress,
        string endpointPrefix,
        GameDebugController control,
        GameDebugFramePublisher frames,
        IGameDebugSnapshotProvider snapshots,
        IGameDebugMutationHandler mutations,
        GameDebugDumpRecorder dumps,
        object debugStateGate,
        int maxConnections)
    {
        _listener = listener;
        _connectionSlots = new SemaphoreSlim(maxConnections, maxConnections);
        _debugStateGate = debugStateGate;
        BaseAddress = baseAddress;
        _endpointPrefix = endpointPrefix;
        _control = control;
        _frames = frames;
        _snapshots = snapshots;
        _mutations = mutations;
        _dumps = dumps;
        _acceptLoop = AcceptLoopAsync();
    }

    public Uri BaseAddress { get; }

    public static Task<GameDebugHost> StartAsync(CancellationToken cancellationToken = default)
    {
        return StartAsync(null, cancellationToken);
    }

    public static Task<GameDebugHost> StartAsync(
        Action<GameDebugHostOptions>? configure,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hostOptions = new GameDebugHostOptions();
        configure?.Invoke(hostOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostOptions.Url);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostOptions.EndpointPrefix);
        if (hostOptions.MaxConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hostOptions.MaxConnections),
                "The debug host must allow at least one connection.");
        }

        var listenAddress = CreateListenAddress(hostOptions.Url);
        var endpointPrefix = NormalizeEndpointPrefix(hostOptions.EndpointPrefix);
        var listener = new TcpListener(listenAddress.Address, listenAddress.Port);
        listener.Start();

        var debugOptions = new GameDebugOptions
        {
            EnableMutations = hostOptions.EnableMutations,
        };
        var session = new GameDebugSession();
        var debugStateGate = new object();
        var control = GameDebugController.Shared;
        var frames = GameDebugFramePublisher.Shared;
        var serializer = new OdinGameDebugComponentValueSerializer();
        var snapshots = new SynchronizedGameDebugSnapshotProvider(
            new GameDebugSnapshotProvider(session, serializer),
            debugStateGate);
        var mutations = new GameDebugMutationHandler(session, debugOptions, serializer);
        var dumps = new GameDebugDumpRecorder(snapshots, control, frames, debugOptions);

        var host = new GameDebugHost(
            listener,
            CreateBaseAddress(hostOptions.Url, listener),
            endpointPrefix,
            control,
            frames,
            snapshots,
            mutations,
            dumps,
            debugStateGate,
            hostOptions.MaxConnections);
        return Task.FromResult(host);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _listener.Stop();

        try
        {
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }

            await WaitForConnectionsAsync();
            _dumps.Dispose();
        }

        finally
        {
            _connectionSlots.Dispose();
            _shutdown.Dispose();
        }
    }

    private void StartConnection(TcpClient client, CancellationToken cancellationToken)
    {
        lock (_connectionGate)
        {
            _activeConnectionCount++;
        }

        HandleTrackedClientAsync(client, cancellationToken).Forget();
    }

    private async UniTaskVoid HandleTrackedClientAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(client, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            CompleteConnection();
        }
    }

    private UniTask WaitForConnectionsAsync()
    {
        lock (_connectionGate)
        {
            if (_activeConnectionCount == 0)
            {
                return UniTask.CompletedTask;
            }

            _connectionsDrained ??= new UniTaskCompletionSource();
            return _connectionsDrained.Task;
        }
    }

    private void CompleteConnection()
    {
        lock (_connectionGate)
        {
            _activeConnectionCount--;
            if (_activeConnectionCount != 0)
            {
                return;
            }

            _connectionsDrained?.TrySetResult();
            _connectionsDrained = null;
        }
    }

    private static async UniTask TryWriteErrorAsync(
        TcpClient client,
        NetworkStream stream,
        int statusCode,
        string reasonPhrase,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteJsonAsync(
                stream,
                statusCode,
                reasonPhrase,
                new DebugErrorResponse(message),
                cancellationToken);
            TryShutdown(client.Client);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
    }

    private async UniTask AcceptLoopAsync()
    {
        var cancellationToken = _shutdown.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            await _connectionSlots.WaitAsync(cancellationToken).ConfigureAwait(false);

            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _connectionSlots.Release();
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                _connectionSlots.Release();
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                _connectionSlots.Release();
                break;
            }

            StartConnection(client, cancellationToken);
        }
    }

    private async UniTask HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var clientScope = client;
            using var stream = client.GetStream();
            try
            {
                var request = await ReadRequestAsync(stream, cancellationToken);
                if (request is null)
                {
                    return;
                }

                await DispatchAsync(stream, request, cancellationToken);
                TryShutdown(client.Client);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (JsonException exception)
            {
                await TryWriteErrorAsync(
                    client,
                    stream,
                    400,
                    "Bad Request",
                    exception.Message,
                    cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                await TryWriteErrorAsync(
                    client,
                    stream,
                    400,
                    "Bad Request",
                    exception.Message,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                await TryWriteErrorAsync(
                    client,
                    stream,
                    500,
                    "Internal Server Error",
                    exception.Message,
                    cancellationToken);
            }
        }
        finally
        {
            _connectionSlots.Release();
        }
    }

    private static void TryShutdown(Socket socket)
    {
        try
        {
            socket.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async UniTask DispatchAsync(
        NetworkStream stream,
        DebugHttpRequest request,
        CancellationToken cancellationToken)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(request.Method, "OPTIONS"))
        {
            await WriteNoContentAsync(stream, cancellationToken);
            return;
        }

        if (!TryGetEndpoint(request.Path, out var endpoint))
        {
            await WriteJsonAsync(
                stream,
                404,
                "Not Found",
                new DebugErrorResponse("Debug endpoint was not found."),
                cancellationToken);
            return;
        }

        if (IsGet(request, endpoint, "/health"))
        {
            await WriteJsonAsync(
                stream,
                200,
                "OK",
                new DebugHealthResponse("ok", DateTimeOffset.UtcNow),
                cancellationToken);
            return;
        }

        if (IsGet(request, endpoint, "/snapshot"))
        {
            var message = await CaptureSnapshotOnNextFrameAsync(
                CreateSnapshotCaptureOptions(request.Query),
                cancellationToken);
            await WriteJsonAsync(
                stream,
                200,
                "OK",
                message,
                cancellationToken);
            return;
        }

        if (IsGet(request, endpoint, "/stream"))
        {
            await StreamSnapshotsAsync(stream, request.Query, cancellationToken);
            return;
        }

        if (IsGet(request, endpoint, "/control"))
        {
            await WriteJsonAsync(
                stream,
                200,
                "OK",
                ExecuteDebugOperation(_control.GetState),
                cancellationToken);
            return;
        }

        if (IsPost(request, endpoint, "/control"))
        {
            var body = ReadJsonBody<GameDebugControlRequest>(request);
            await WriteJsonAsync(
                stream,
                200,
                "OK",
                ExecuteDebugOperation(() => _control.Execute(body)),
                cancellationToken);
            return;
        }

        if (IsPost(request, endpoint, "/mutations"))
        {
            var body = ReadJsonBody<GameDebugMutationRequest>(request);
            var result = await ExecuteMutationOnNextFrameAsync(body, cancellationToken);
            await WriteJsonAsync(
                stream,
                200,
                "OK",
                result,
                cancellationToken);
            return;
        }

        if (IsGet(request, endpoint, "/dump/recording"))
        {
            await WriteJsonAsync(
                stream,
                200,
                "OK",
                ExecuteDebugOperation(_dumps.GetState),
                cancellationToken);
            return;
        }

        if (IsPost(request, endpoint, "/dump/recording"))
        {
            var body = ReadJsonBody<GameDebugDumpRecordingRequest>(request);
            await WriteJsonAsync(
                stream,
                200,
                "OK",
                ExecuteDebugOperation(() => _dumps.Execute(body)),
                cancellationToken);
            return;
        }

        await WriteJsonAsync(
            stream,
            405,
            "Method Not Allowed",
            new DebugErrorResponse("Debug endpoint does not support this method."),
            cancellationToken);
    }

    private async UniTask<GameDebugSnapshotMessage> CaptureSnapshotOnNextFrameAsync(
        GameDebugSnapshotCaptureOptions captureOptions,
        CancellationToken cancellationToken)
    {
        var completion = new UniTaskCompletionSource<GameDebugSnapshotMessage>();

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
        using var cancellationRegistration = cancellationToken.Register(() =>
            completion.TrySetException(new OperationCanceledException(cancellationToken)));
        try
        {
            return await completion.Task;
        }
        finally
        {
            _frames.FramePublished -= OnFramePublished;
        }
    }

    private async UniTask<GameDebugMutationResult> ExecuteMutationOnNextFrameAsync(
        GameDebugMutationRequest request,
        CancellationToken cancellationToken)
    {
        var completion = new UniTaskCompletionSource<GameDebugMutationResult>();

        void OnFrameEnding(GameDebugFrameInfo info)
        {
            _frames.FrameEnding -= OnFrameEnding;
            try
            {
                completion.TrySetResult(ExecuteDebugOperation(() => _mutations.Execute(request)));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        _frames.FrameEnding += OnFrameEnding;
        using var cancellationRegistration = cancellationToken.Register(() =>
            completion.TrySetException(new OperationCanceledException(cancellationToken)));
        try
        {
            return await completion.Task;
        }
        finally
        {
            _frames.FrameEnding -= OnFrameEnding;
        }
    }

    private async UniTask StreamSnapshotsAsync(
        NetworkStream stream,
        IReadOnlyDictionary<string, string> query,
        CancellationToken cancellationToken)
    {
        var captureOptions = CreateSnapshotCaptureOptions(query) with
        {
            IncludeComponentPayloads = false,
            IncludeStructuredComponentValues = false,
        };
        var channel = System.Threading.Channels.Channel.CreateBounded<GameDebugSnapshotMessage>(new BoundedChannelOptions(16)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        void OnFramePublished(GameDebugFrameInfo info)
        {
            var message = CaptureSnapshotMessage(info, captureOptions);
            channel.Writer.TryWrite(message);
        }

        await WriteStreamHeadersAsync(stream, cancellationToken);

        _frames.FramePublished += OnFramePublished;
        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await WriteServerSentEventAsync(stream, "snapshot", message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            _frames.FramePublished -= OnFramePublished;
            channel.Writer.TryComplete();
        }
    }

    private GameDebugSnapshotMessage CaptureSnapshotMessage(
        GameDebugFrameInfo frame,
        GameDebugSnapshotCaptureOptions captureOptions)
    {
        return ExecuteDebugOperation(() => new GameDebugSnapshotMessage(
            frame,
            _snapshots.Capture(captureOptions),
            _control.GetState()));
    }

    private T ExecuteDebugOperation<T>(Func<T> operation)
    {
        lock (_debugStateGate)
        {
            return operation();
        }
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

    private static async UniTask<DebugHttpRequest?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var headers = await ReadHeaderTextAsync(stream, cancellationToken);
        if (headers is null)
        {
            return null;
        }

        var lines = headers.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            throw new InvalidDataException("The request line was empty.");
        }

        var requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
        {
            throw new InvalidDataException("The request line was malformed.");
        }

        var headerValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            headerValues[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        var body = IsChunked(headerValues)
            ? await ReadChunkedBodyAsync(stream, cancellationToken)
            : await ReadContentLengthBodyAsync(stream, headerValues, cancellationToken);
        var target = ParseTarget(requestLine[1]);

        return new DebugHttpRequest(
            requestLine[0].ToUpperInvariant(),
            target.Path,
            target.Query,
            body);
    }

    private static async UniTask<byte[]> ReadContentLengthBodyAsync(
        NetworkStream stream,
        IReadOnlyDictionary<string, string> headerValues,
        CancellationToken cancellationToken)
    {
        var contentLength = GetContentLength(headerValues);
        if (contentLength > MaxBodyBytes)
        {
            throw new InvalidDataException("The debug request body was too large.");
        }

        return contentLength > 0
            ? await ReadBodyAsync(stream, contentLength, cancellationToken)
            : [];
    }

    private static async UniTask<string?> ReadHeaderTextAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var previous = new Queue<byte>(4);
        var buffer = new byte[1];

        while (bytes.Count < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0
                    ? null
                    : throw new InvalidDataException("The HTTP request ended before headers completed.");
            }

            bytes.Add(buffer[0]);
            previous.Enqueue(buffer[0]);
            while (previous.Count > 4)
            {
                previous.Dequeue();
            }

            if (previous.Count == 4 && previous.SequenceEqual(HeaderTerminator))
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        throw new InvalidDataException("The debug request headers were too large.");
    }

    private static async UniTask<byte[]> ReadBodyAsync(
        NetworkStream stream,
        int contentLength,
        CancellationToken cancellationToken)
    {
        var body = new byte[contentLength];
        var offset = 0;
        while (offset < body.Length)
        {
            var read = await stream.ReadAsync(
                body.AsMemory(offset, body.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("The HTTP request ended before the body completed.");
            }

            offset += read;
        }

        return body;
    }

    private static async UniTask<byte[]> ReadChunkedBodyAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadAsciiLineAsync(stream, cancellationToken);
            var extensionStart = sizeLine.IndexOf(';');
            var sizeText = extensionStart >= 0 ? sizeLine[..extensionStart] : sizeLine;
            if (!int.TryParse(
                    sizeText.Trim(),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var chunkSize)
                || chunkSize < 0)
            {
                throw new InvalidDataException("The chunked debug request body was malformed.");
            }

            if (chunkSize == 0)
            {
                while (true)
                {
                    var trailer = await ReadAsciiLineAsync(stream, cancellationToken);
                    if (trailer.Length == 0)
                    {
                        return body.ToArray();
                    }
                }
            }

            if (body.Length + chunkSize > MaxBodyBytes)
            {
                throw new InvalidDataException("The debug request body was too large.");
            }

            var chunk = await ReadBodyAsync(stream, chunkSize, cancellationToken);
            await body.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);

            var ending = await ReadBodyAsync(stream, 2, cancellationToken);
            if (ending[0] != 13 || ending[1] != 10)
            {
                throw new InvalidDataException("The chunked debug request body was malformed.");
            }
        }
    }

    private static async UniTask<string> ReadAsciiLineAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (bytes.Count < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("The HTTP request ended before a line completed.");
            }

            if (buffer[0] == 10)
            {
                if (bytes.Count > 0 && bytes[^1] == 13)
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add(buffer[0]);
        }

        throw new InvalidDataException("The debug request line was too large.");
    }

    private static int GetContentLength(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Content-Length", out var value))
        {
            return 0;
        }

        return int.TryParse(value, out var contentLength) && contentLength >= 0
            ? contentLength
            : throw new InvalidDataException("The Content-Length header was invalid.");
    }

    private static bool IsChunked(IReadOnlyDictionary<string, string> headers)
    {
        return headers.TryGetValue("Transfer-Encoding", out var value)
            && value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(static encoding => StringComparer.OrdinalIgnoreCase.Equals(encoding, "chunked"));
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

    private static T ReadJsonBody<T>(DebugHttpRequest request)
    {
        if (request.Body.Length == 0)
        {
            throw new JsonException("The debug request body was empty.");
        }

        return JsonSerializer.Deserialize<T>(request.Body, GameDebugJson.Options)
            ?? throw new JsonException($"The debug request body could not be deserialized as {typeof(T).Name}.");
    }

    private static async UniTask WriteNoContentAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var headers = CreateHeaders(204, "No Content", "text/plain; charset=utf-8", 0, closeConnection: true);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async UniTask WriteJsonAsync(
        NetworkStream stream,
        int statusCode,
        string reasonPhrase,
        object value,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, GameDebugJson.Options);
        var headers = CreateHeaders(
            statusCode,
            reasonPhrase,
            "application/json; charset=utf-8",
            body.Length,
            closeConnection: true);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async UniTask WriteStreamHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var headers = new StringBuilder()
            .Append("HTTP/1.1 200 OK\r\n")
            .Append("Content-Type: text/event-stream\r\n")
            .Append("Cache-Control: no-cache\r\n")
            .Append("Connection: keep-alive\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Headers: content-type\r\n")
            .Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n")
            .Append("\r\n")
            .ToString();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async UniTask WriteServerSentEventAsync(
        NetworkStream stream,
        string eventName,
        object value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, GameDebugJson.Options);
        var data = Encoding.UTF8.GetBytes($"event: {eventName}\n" + $"data: {json}\n\n");
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string CreateHeaders(
        int statusCode,
        string reasonPhrase,
        string contentType,
        int contentLength,
        bool closeConnection)
    {
        return new StringBuilder()
            .Append("HTTP/1.1 ")
            .Append(statusCode)
            .Append(' ')
            .Append(reasonPhrase)
            .Append("\r\n")
            .Append("Content-Type: ")
            .Append(contentType)
            .Append("\r\n")
            .Append("Content-Length: ")
            .Append(contentLength)
            .Append("\r\n")
            .Append("Cache-Control: no-cache\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Headers: content-type\r\n")
            .Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n")
            .Append(closeConnection ? "Connection: close\r\n" : string.Empty)
            .Append("\r\n")
            .ToString();
    }

    private static GameDebugSnapshotCaptureOptions CreateSnapshotCaptureOptions(
        IReadOnlyDictionary<string, string> query)
    {
        return new GameDebugSnapshotCaptureOptions
        {
            WorldName = GetString(query, "world") ?? GetString(query, "worldName"),
            SceneName = GetString(query, "scene") ?? GetString(query, "sceneName"),
            EntityId = GetInt(query, "entityId"),
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
        return query.TryGetValue(key, out var value) && int.TryParse(value, out var result)
            ? result
            : null;
    }

    private static int? NormalizeLimit(int? value)
    {
        return value is > 0 ? value : null;
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

    private static bool IsGet(DebugHttpRequest request, string endpoint, string expectedEndpoint)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(request.Method, "GET")
            && StringComparer.Ordinal.Equals(endpoint, expectedEndpoint);
    }

    private static bool IsPost(DebugHttpRequest request, string endpoint, string expectedEndpoint)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(request.Method, "POST")
            && StringComparer.Ordinal.Equals(endpoint, expectedEndpoint);
    }

    private static IPEndPoint CreateListenAddress(string url)
    {
        var uri = CreateUri(url);
        if (!StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The debug host currently supports only http:// loopback URLs.", nameof(url));
        }

        var address = uri.Host switch
        {
            "localhost" => IPAddress.Loopback,
            "127.0.0.1" => IPAddress.Loopback,
            "::1" => IPAddress.IPv6Loopback,
            "0.0.0.0" => IPAddress.Any,
            "*" => IPAddress.Any,
            "+" => IPAddress.Any,
            _ when IPAddress.TryParse(uri.Host, out var parsed) => parsed,
            _ => IPAddress.Loopback,
        };

        return new IPEndPoint(address, uri.Port);
    }

    private static Uri CreateBaseAddress(string url, TcpListener listener)
    {
        var uri = CreateUri(url);
        var localEndpoint = listener.LocalEndpoint as IPEndPoint
            ?? throw new InvalidOperationException("The debug host did not expose a TCP endpoint.");
        var host = uri.Host switch
        {
            "*" or "+" or "0.0.0.0" or "::" => "127.0.0.1",
            _ => uri.Host,
        };

        if (IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            host = $"[{address}]";
        }

        return new Uri($"http://{host}:{localEndpoint.Port}/");
    }

    private static Uri CreateUri(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Port >= 0
            ? uri
            : throw new ArgumentException("The debug host URL must be an absolute URL with a port.", nameof(url));
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

    private sealed record DebugHttpRequest(
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Query,
        byte[] Body);

    private sealed record DebugHttpTarget(
        string Path,
        IReadOnlyDictionary<string, string> Query);

    private sealed class SynchronizedGameDebugSnapshotProvider : IGameDebugSnapshotProvider
    {
        private readonly IGameDebugSnapshotProvider _inner;
        private readonly object _gate;

        public SynchronizedGameDebugSnapshotProvider(
            IGameDebugSnapshotProvider inner,
            object gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public GameDebugSnapshot Capture(GameDebugSnapshotCaptureOptions? options = null)
        {
            lock (_gate)
            {
                return _inner.Capture(options);
            }
        }
    }

    private sealed record DebugHealthResponse(
        string Status,
        DateTimeOffset CapturedAt);

    private sealed record DebugErrorResponse(string Message);
}
