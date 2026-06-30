using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Adapter.Godot;

public sealed class GodotDebugEndpointBridge : IDisposable
{
    private readonly GameDebugEndpointTextBridge _bridge;

    public GodotDebugEndpointBridge(GodotDebugEndpointBridgeOptions? options = null)
        : this(new GameDebugEndpointTextBridge(CreateDispatcherOptions(options)))
    {
    }

    public GodotDebugEndpointBridge(GameDebugEndpointTextBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public string Handle(string request)
    {
        return _bridge.Handle(request);
    }

    public void Dispose()
    {
        _bridge.Dispose();
    }

    public static GameDebugEndpointDispatcherOptions CreateDispatcherOptions(
        GodotDebugEndpointBridgeOptions? options = null)
    {
        options ??= new GodotDebugEndpointBridgeOptions();
        return new GameDebugEndpointDispatcherOptions
        {
            EndpointPrefix = options.EndpointPrefix,
            DefaultWaitForSnapshotFrame = options.DefaultWaitForSnapshotFrame,
            EnableMutations = options.EnableMutations,
            DumpDirectory = options.DumpDirectory,
            MaxRecordedDumpFrames = options.MaxRecordedDumpFrames,
            Session = options.Session,
            Control = options.Control ?? GameDebugController.Shared,
            Frames = options.Frames ?? GameDebugFramePublisher.Shared,
            ComponentValueSerializer = options.ComponentValueSerializer,
            DebugStateGate = options.DebugStateGate,
        };
    }
}

public sealed class GodotDebugEndpointBridgeOptions
{
    public string EndpointPrefix { get; set; } = "/_nkg/debug";

    public bool DefaultWaitForSnapshotFrame { get; set; }

    public bool EnableMutations { get; set; } = true;

    public string? DumpDirectory { get; set; }

    public int? MaxRecordedDumpFrames { get; set; }

    public GameDebugSession? Session { get; set; }

    public GameDebugController? Control { get; set; }

    public GameDebugFramePublisher? Frames { get; set; }

    public IGameDebugComponentValueSerializer? ComponentValueSerializer { get; set; }

    public object? DebugStateGate { get; set; }
}
