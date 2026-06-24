namespace NKGGameFramework.Hosting.Diagnostics;

public static class GameDebugHostAutoStart
{
    public const string EnabledVariable = "NKG_DEBUG_HOST";
    public const string UrlVariable = "NKG_DEBUG_HOST_URL";
    public const string EndpointPrefixVariable = "NKG_DEBUG_HOST_PREFIX";
    public const string EnableMutationsVariable = "NKG_DEBUG_HOST_MUTATIONS";

    private static readonly object Gate = new();
    private static GameDebugHost? _host;
    private static Task<GameDebugHost?>? _startTask;

    public static bool IsEnabled => IsTruthy(Environment.GetEnvironmentVariable(EnabledVariable));

    public static Uri? BaseAddress
    {
        get
        {
            lock (Gate)
            {
                return _host?.BaseAddress;
            }
        }
    }

    public static Task<GameDebugHost?> TryStartFromEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.FromResult<GameDebugHost?>(null);
        }

        lock (Gate)
        {
            if (_host is not null)
            {
                return Task.FromResult<GameDebugHost?>(_host);
            }

            _startTask ??= StartCoreAsync(cancellationToken);
            return _startTask;
        }
    }

    public static async ValueTask StopAsync()
    {
        Task<GameDebugHost?>? pendingStart;
        lock (Gate)
        {
            pendingStart = _startTask;
        }

        if (pendingStart is not null)
        {
            await pendingStart.ConfigureAwait(false);
        }

        GameDebugHost? host;
        lock (Gate)
        {
            host = _host;
            _host = null;
            _startTask = null;
        }

        if (host is not null)
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static bool IsTruthy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false,
        };
    }

    private static async Task<GameDebugHost?> StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var host = await GameDebugHost.StartAsync(ConfigureFromEnvironment, cancellationToken)
                .ConfigureAwait(false);

            lock (Gate)
            {
                _host = host;
                _startTask = null;
            }

            return host;
        }
        catch
        {
            lock (Gate)
            {
                _startTask = null;
            }

            throw;
        }
    }

    private static void ConfigureFromEnvironment(GameDebugHostOptions options)
    {
        var url = Environment.GetEnvironmentVariable(UrlVariable);
        if (!string.IsNullOrWhiteSpace(url))
        {
            options.Url = url;
        }

        var prefix = Environment.GetEnvironmentVariable(EndpointPrefixVariable);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            options.EndpointPrefix = prefix;
        }

        var mutations = Environment.GetEnvironmentVariable(EnableMutationsVariable);
        if (!string.IsNullOrWhiteSpace(mutations))
        {
            options.EnableMutations = IsTruthy(mutations);
        }
    }
}
