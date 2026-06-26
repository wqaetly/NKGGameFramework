namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugHostOptions
{
    public string Url { get; set; } = "http://127.0.0.1:5067";

    public string EndpointPrefix { get; set; } = "/_nkg/debug";

    public bool EnableMutations { get; set; }

    public int MaxConnections { get; set; } = 32;
}
