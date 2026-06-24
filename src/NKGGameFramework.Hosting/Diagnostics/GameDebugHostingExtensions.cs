using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NKGGameFramework.Diagnostics;
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
        services.TryAddSingleton(GameDebugController.Shared);
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

        group.MapGet("/snapshot", (
            HttpContext httpContext,
            IGameDebugSnapshotProvider snapshots) =>
            Results.Json(
                snapshots.Capture(CreateSnapshotCaptureOptions(httpContext.Request.Query)),
                GameDebugJson.Options));

        group.MapGet("/control", (GameDebugController control) =>
            Results.Json(control.GetState(), GameDebugJson.Options));

        group.MapPost("/control", (
            GameDebugControlRequest request,
            GameDebugController control) =>
            Results.Json(control.Execute(request), GameDebugJson.Options));

        group.MapPost("/mutations", (
            GameDebugMutationRequest request,
            IGameDebugMutationHandler mutations) =>
            Results.Json(mutations.Execute(request), GameDebugJson.Options));

        return endpoints;
    }

    private static GameDebugSnapshotCaptureOptions CreateSnapshotCaptureOptions(IQueryCollection query)
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

    private static string? GetString(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int? GetInt(IQueryCollection query, string key)
    {
        return query.TryGetValue(key, out var value) && int.TryParse(value.ToString(), out var result)
            ? result
            : null;
    }

    private static int? NormalizeLimit(int? value)
    {
        return value is > 0 ? value : null;
    }

    private static bool? GetBool(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value.ToString();
        if (bool.TryParse(text, out var result))
        {
            return result;
        }

        return text.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => null,
        };
    }
}
