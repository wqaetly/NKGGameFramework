using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NKGGameFramework.Hosting.Diagnostics;

public static class GameDebugHostingExtensions
{
    public static IServiceCollection AddNkgGameDebugging(
        this IServiceCollection services,
        Action<GameDebugOptions>? configure = null)
    {
        services.AddOptions<GameDebugOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<GameDebugSession>();
        services.TryAddSingleton<IGameDebugComponentValueSerializer, OdinGameDebugComponentValueSerializer>();
        services.TryAddSingleton<IGameDebugSnapshotProvider, GameDebugSnapshotProvider>();
        services.TryAddSingleton<IGameDebugMutationHandler, GameDebugMutationHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapNkgGameDebugEndpoints(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/_nkg/debug")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var group = endpoints.MapGroup(prefix);

        group.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            capturedAt = DateTimeOffset.UtcNow,
        }, GameDebugJson.Options));

        group.MapGet("/snapshot", (IGameDebugSnapshotProvider snapshots) =>
            Results.Json(snapshots.Capture(), GameDebugJson.Options));

        group.MapPost("/mutations", (
            GameDebugMutationRequest request,
            IGameDebugMutationHandler mutations) =>
            Results.Json(mutations.Execute(request), GameDebugJson.Options));

        return endpoints;
    }
}
