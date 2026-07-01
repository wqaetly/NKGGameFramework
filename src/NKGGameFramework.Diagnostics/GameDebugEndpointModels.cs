namespace NKGGameFramework.Diagnostics;

public sealed record GameDebugEndpointRequest(
    string Method,
    string Target,
    byte[] Body);

public sealed record GameDebugEndpointResponse(
    int StatusCode,
    string ReasonPhrase,
    string ContentType,
    byte[] Body)
{
    public string BodyText => System.Text.Encoding.UTF8.GetString(Body);
}

public sealed class GameDebugEndpointDispatcherOptions
{
    public string EndpointPrefix { get; set; } = "/_nkg/debug";

    public bool DefaultWaitForSnapshotFrame { get; set; } = true;

    public bool EnableMutations { get; set; }

    public string? DumpDirectory { get; set; }

    public GameDebugSession? Session { get; set; }

    public GameDebugController? Control { get; set; }

    public GameDebugFramePublisher? Frames { get; set; }

    public IGameDebugComponentValueSerializer? ComponentValueSerializer { get; set; }

    public IGameDebugSnapshotProvider? Snapshots { get; set; }

    public IGameDebugMutationHandler? Mutations { get; set; }

    public GameDebugDumpRecorder? Dumps { get; set; }

    public object? DebugStateGate { get; set; }
}
