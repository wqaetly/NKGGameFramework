namespace NKGGameFramework.Hosting.Diagnostics;

public sealed record GameDebugHostStartupOptions
{
    public static GameDebugHostStartupOptions Disabled { get; } = new();

    public static GameDebugHostStartupOptions Localhost(
        int port = 5067,
        bool enableMutations = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);

        return new GameDebugHostStartupOptions
        {
            Enabled = true,
            Url = $"http://127.0.0.1:{port}",
            EnableMutations = enableMutations,
        };
    }

    public bool Enabled { get; init; }

    public string? Url { get; init; }

    public string? EndpointPrefix { get; init; }

    public bool? EnableMutations { get; init; }

    public int? MaxConnections { get; init; }

    internal void ApplyTo(GameDebugHostOptions options)
    {
        if (!string.IsNullOrWhiteSpace(Url))
        {
            options.Url = Url;
        }

        if (!string.IsNullOrWhiteSpace(EndpointPrefix))
        {
            options.EndpointPrefix = EndpointPrefix;
        }

        if (EnableMutations is { } enableMutations)
        {
            options.EnableMutations = enableMutations;
        }

        if (MaxConnections is { } maxConnections)
        {
            options.MaxConnections = maxConnections;
        }
    }
}
