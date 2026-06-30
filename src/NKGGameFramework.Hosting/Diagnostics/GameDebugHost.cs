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
    private const int MaxBodyBytes = 128 * 1024 * 1024;
    private static readonly byte[] HeaderTerminator = [13, 10, 13, 10];

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _connectionSlots;
    private readonly object _connectionGate = new();
    private readonly UniTask _acceptLoop;
    private readonly string _endpointPrefix;
    private readonly GameDebugFramePublisher _frames;
    private readonly GameDebugDumpRecorder _dumps;
    private readonly GameDebugEndpointDispatcher _dispatcher;
    private int _activeConnectionCount;
    private UniTaskCompletionSource? _connectionsDrained;
    private bool _disposed;

    private GameDebugHost(
        TcpListener listener,
        Uri baseAddress,
        string endpointPrefix,
        GameDebugFramePublisher frames,
        GameDebugDumpRecorder dumps,
        GameDebugEndpointDispatcher dispatcher,
        int maxConnections)
    {
        _listener = listener;
        _connectionSlots = new SemaphoreSlim(maxConnections, maxConnections);
        BaseAddress = baseAddress;
        _endpointPrefix = endpointPrefix;
        _frames = frames;
        _dumps = dumps;
        _dispatcher = dispatcher;
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
        var dumps = new GameDebugDumpRecorder(snapshots, control, frames, debugOptions, session, debugStateGate);
        var dispatcher = new GameDebugEndpointDispatcher(new GameDebugEndpointDispatcherOptions
        {
            EndpointPrefix = endpointPrefix,
            DefaultWaitForSnapshotFrame = true,
            EnableMutations = hostOptions.EnableMutations,
            Session = session,
            Control = control,
            Frames = frames,
            ComponentValueSerializer = serializer,
            Snapshots = snapshots,
            Mutations = mutations,
            Dumps = dumps,
            DebugStateGate = debugStateGate,
        });

        var host = new GameDebugHost(
            listener,
            CreateBaseAddress(hostOptions.Url, listener),
            endpointPrefix,
            frames,
            dumps,
            dispatcher,
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
            _dispatcher.Dispose();
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
        if (StringComparer.OrdinalIgnoreCase.Equals(request.Method, "GET") &&
            TryGetEndpoint(request.Path, out var endpoint) &&
            StringComparer.Ordinal.Equals(endpoint, "/stream"))
        {
            await StreamSnapshotsAsync(stream, request.Query, cancellationToken);
            return;
        }

        var response = await _dispatcher.HandleAsync(
            new GameDebugEndpointRequest(request.Method, request.Target, request.Body),
            cancellationToken).ConfigureAwait(false);
        await WriteResponseAsync(stream, response, cancellationToken);
    }

    private async UniTask StreamSnapshotsAsync(
        NetworkStream stream,
        IReadOnlyDictionary<string, string> query,
        CancellationToken cancellationToken)
    {
        var captureOptions = GameDebugEndpointDispatcher.CreateStreamSnapshotCaptureOptions(
            GameDebugEndpointDispatcher.CreateSnapshotCaptureOptions(query));
        var channel = System.Threading.Channels.Channel.CreateBounded<GameDebugSnapshotMessage>(new BoundedChannelOptions(16)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        void OnFramePublished(GameDebugFrameInfo info)
        {
            var message = _dispatcher.CaptureSnapshotMessage(info, captureOptions);
            channel.Writer.TryWrite(message);
        }

        _frames.FramePublished += OnFramePublished;
        try
        {
            await WriteStreamHeadersAsync(stream, cancellationToken);

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
            requestLine[1],
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

    private static async UniTask WriteResponseAsync(
        NetworkStream stream,
        GameDebugEndpointResponse response,
        CancellationToken cancellationToken)
    {
        var headers = CreateHeaders(
            response.StatusCode,
            response.ReasonPhrase,
            response.ContentType,
            response.Body.Length,
            closeConnection: true);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken).ConfigureAwait(false);
        if (response.Body.Length > 0)
        {
            await stream.WriteAsync(response.Body, cancellationToken).ConfigureAwait(false);
        }
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
        string Target,
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

    private sealed record DebugErrorResponse(string Message);
}
