using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private bool _disposed;

    private GameDebugHost(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static Task<GameDebugHost> StartAsync(CancellationToken cancellationToken = default)
    {
        return StartAsync(null, cancellationToken);
    }

    public static async Task<GameDebugHost> StartAsync(
        Action<GameDebugHostOptions>? configure,
        CancellationToken cancellationToken = default)
    {
        var options = new GameDebugHostOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Url);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.EndpointPrefix);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(GameDebugHost).Assembly.FullName,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(options.Url);
        builder.Services.AddNkgGameDebugging(debugOptions =>
        {
            debugOptions.EnableMutations = options.EnableMutations;
        });

        var app = builder.Build();
        app.MapNkgGameDebugEndpoints(options.EndpointPrefix);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        return new GameDebugHost(app, ResolveBaseAddress(app));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static Uri ResolveBaseAddress(WebApplication app)
    {
        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses;
        var address = addresses?.FirstOrDefault() ?? app.Urls.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException("The debug host did not expose a server address.");
        }

        return new Uri(address.EndsWith("/", StringComparison.Ordinal) ? address : $"{address}/");
    }
}
