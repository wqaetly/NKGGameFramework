namespace NKGGameFramework.Hosting.Diagnostics;

public static class GameDebugHostAutoStart
{
    private static readonly object Gate = new();
    private static GameDebugHost? _host;
    private static Task<GameDebugHost?>? _startTask;

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

    public static Task<GameDebugHost?> TryStartAsync(
        GameDebugHostStartupOptions startupOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startupOptions);

        if (!startupOptions.Enabled)
        {
            return Task.FromResult<GameDebugHost?>(null);
        }

        lock (Gate)
        {
            if (_host is not null)
            {
                return Task.FromResult<GameDebugHost?>(_host);
            }

            _startTask ??= StartCoreAsync(startupOptions, cancellationToken);
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

    private static async Task<GameDebugHost?> StartCoreAsync(
        GameDebugHostStartupOptions startupOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            var host = await GameDebugHost.StartAsync(startupOptions.ApplyTo, cancellationToken)
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
}
