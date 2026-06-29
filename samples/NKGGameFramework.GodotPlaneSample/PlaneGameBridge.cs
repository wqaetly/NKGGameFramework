using System.Globalization;
using System.Text;
using NKGGameFramework.Core;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.GodotPlaneSample;

public static class PlaneGameBridge
{
    private const double StepSeconds = 1.0d / PlaneGameRules.SimulationHz;
    private static PlaneGame? s_session;
    private static PlaneGameDebugEndpoint? s_debug;
    private static int s_moveX;
    private static int s_moveY;
    private static bool s_fire;

    public static void ResetSession()
    {
        s_session?.Dispose();
        s_session = new PlaneGame();
        s_debug = new PlaneGameDebugEndpoint(s_session);
        s_session.Start();
        ClearInput();
    }

    public static void ClearInput()
    {
        s_moveX = 0;
        s_moveY = 0;
        s_fire = false;
    }

    public static void PressLeft() => s_moveX--;

    public static void PressRight() => s_moveX++;

    public static void PressUp() => s_moveY--;

    public static void PressDown() => s_moveY++;

    public static void PressFire() => s_fire = true;

    public static string StepSession()
    {
        s_session ??= CreateStartedSession();
        s_debug ??= new PlaneGameDebugEndpoint(s_session);
        if (s_session.IsGameOver)
        {
            return s_session.CreateSnapshot();
        }

        s_session.SetInput(ClampAxis(s_moveX), ClampAxis(s_moveY), s_fire);
        s_session.Update(StepSeconds);
        return s_session.CreateSnapshot();
    }

    public static string GetSessionStatus()
    {
        if (s_session is null)
        {
            return "idle";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"score={s_session.Score} lives={s_session.Lives} game_over={s_session.IsGameOver}");
    }

    public static string HandleDebugRequest(string request)
    {
        s_session ??= CreateStartedSession();
        s_debug ??= new PlaneGameDebugEndpoint(s_session);
        return s_debug.Handle(request);
    }

    private static PlaneGame CreateStartedSession()
    {
        var game = new PlaneGame();
        s_debug = new PlaneGameDebugEndpoint(game);
        game.Start();
        return game;
    }

    private static int ClampAxis(int value)
    {
        if (value < -1)
        {
            return -1;
        }

        return value > 1 ? 1 : value;
    }

    private sealed class PlaneGameDebugEndpoint
    {
        private const string EndpointPrefix = "/_nkg/debug";
        private readonly PlaneGame _game;
        private readonly object _gate = new();
        private readonly GameDebugController _control = GameDebugController.Shared;
        private readonly GameDebugFramePublisher _frames = GameDebugFramePublisher.Shared;

        public PlaneGameDebugEndpoint(PlaneGame game)
        {
            _game = game;
        }

        public string Handle(string request)
        {
            var phase = "start";
            try
            {
                phase = "parse";
                var parsed = ParseRequest(request);
                phase = "options";
                if (string.Equals(parsed.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    return Response(204, "No Content", "text/plain; charset=utf-8", string.Empty);
                }

                phase = "endpoint";
                if (!TryGetEndpoint(parsed.Target.Path, out var endpoint))
                {
                    return JsonResponse(404, "Not Found", ErrorJson("Debug endpoint was not found."));
                }

                phase = "health";
                if (IsGet(parsed, endpoint, "/health"))
                {
                    return JsonResponse(200, "OK", HealthJson());
                }

                phase = "snapshot";
                if (IsGet(parsed, endpoint, "/snapshot") || IsGet(parsed, endpoint, "/stream"))
                {
                    var options = CreateSnapshotCaptureOptions(parsed.Target.Query);
                    if (StringComparer.Ordinal.Equals(endpoint, "/stream"))
                    {
                        options = options with
                        {
                            IncludeComponentPayloads = false,
                            IncludeStructuredComponentValues = false,
                        };
                    }

                    phase = "snapshot-json";
                    return JsonResponse(200, "OK", SnapshotMessageJson(CaptureSnapshotMessage(options)));
                }

                phase = "control-get";
                if (IsGet(parsed, endpoint, "/control"))
                {
                    return JsonResponse(200, "OK", ControlStateJson(_control.GetState()));
                }

                phase = "control-post";
                if (IsPost(parsed, endpoint, "/control"))
                {
                    var command = ExtractJsonString(parsed.Body, "command") ?? string.Empty;
                    var stepCount = ExtractJsonInt(parsed.Body, "stepCount");
                    return JsonResponse(
                        200,
                        "OK",
                        ControlResultJson(_control.Execute(new GameDebugControlRequest(command, stepCount))));
                }

                phase = "mutations";
                if (IsPost(parsed, endpoint, "/mutations"))
                {
                    return JsonResponse(
                        200,
                        "OK",
                        MutationResultJson(false, "Godot LeanCLR debug bridge currently exposes live inspection and playback control; component mutation JSON parsing stays out of the LeanCLR transport path."));
                }

                phase = "dump-recording";
                if (IsGet(parsed, endpoint, "/dump/recording"))
                {
                    return JsonResponse(200, "OK", RecordingStateJson());
                }

                if (endpoint.StartsWith("/dump/", StringComparison.Ordinal))
                {
                    return JsonResponse(
                        501,
                        "Not Implemented",
                        ErrorJson("Dump playback and binary upload are still served by the managed Hosting transport; the Godot LeanCLR bridge exposes live WebDebug inspection/control first."));
                }

                return JsonResponse(405, "Method Not Allowed", ErrorJson("Debug endpoint does not support this method."));
            }
            catch (InvalidDataException exception)
            {
                return JsonResponse(400, "Bad Request", ErrorJson(phase + ": " + exception.Message));
            }
            catch (Exception exception)
            {
                return JsonResponse(500, "Internal Server Error", ErrorJson(phase + ": " + exception.Message));
            }
        }

        private GameDebugSnapshotMessage CaptureSnapshotMessage(GameDebugSnapshotCaptureOptions options)
        {
            var frame = _frames.GetLastPublished()
                ?? new GameDebugFrameInfo(0, nameof(RuntimeContext), _game.Runtime.Time.Frame, DateTimeOffset.UtcNow);
            return new GameDebugSnapshotMessage(frame, CaptureLiveSnapshot(options), _control.GetState());
        }

        private GameDebugSnapshot CaptureLiveSnapshot(GameDebugSnapshotCaptureOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.WorldName) &&
                !StringComparer.Ordinal.Equals(options.WorldName, _game.World.Name))
            {
                return new GameDebugSnapshot(DateTimeOffset.UtcNow, [CreateRuntimeSnapshot()], []);
            }

            var scenes = _game.World.Scenes
                .Where(scene => string.IsNullOrWhiteSpace(options.SceneName) || StringComparer.Ordinal.Equals(options.SceneName, scene.Name))
                .Select(scene => CaptureScene(scene, options))
                .ToArray();

            return new GameDebugSnapshot(
                DateTimeOffset.UtcNow,
                [CreateRuntimeSnapshot()],
                [new WorldDebugSnapshot(_game.World.Name, scenes.Length, scenes)]);
        }

        private static RuntimeContextDebugSnapshot CreateRuntimeSnapshot()
        {
            return new RuntimeContextDebugSnapshot(0, false, [], []);
        }

        private static SceneDebugSnapshot CaptureScene(Scene scene, GameDebugSnapshotCaptureOptions options)
        {
            var entities = scene.Entities.Where(entity => options.EntityId is null || entity.Id.Value == options.EntityId.Value);
            if (options.EntityId is null)
            {
                if (options.EntityOffset > 0)
                {
                    entities = entities.Skip(options.EntityOffset);
                }

                if (options.EntityLimit is { } limit)
                {
                    entities = entities.Take(limit);
                }
            }

            return new SceneDebugSnapshot(
                scene.Name,
                scene.EntityCount,
                scene.Systems.Systems.Select(static system => new SystemDebugSnapshot(ToDebugType(system.GetType()), system.Order, system.Enabled)).ToArray(),
                scene.ComponentStores.Select(static store => new ComponentStoreDebugSnapshot(ToDebugType(store.ComponentType), store.Count, store.EntityIds)).ToArray(),
                entities.Select(entity => CaptureEntity(scene, entity, options)).ToArray());
        }

        private static EntityDebugSnapshot CaptureEntity(Scene scene, Entity entity, GameDebugSnapshotCaptureOptions options)
        {
            var components = scene.GetComponents(entity)
                .Where(component => MatchesComponent(component.ComponentType, options))
                .Select(component => new ComponentDebugSnapshot(
                    ToDebugType(component.ComponentType),
                    new ComponentValueDebugSnapshot("none", null, null),
                    new ComponentGraphDebugSnapshot(CreateComponentGraphId(component.ComponentType), null, null, null, 0)))
                .ToArray();

            return new EntityDebugSnapshot(entity.Id.Value, entity.Version, components, [], []);
        }

        private static bool MatchesComponent(Type componentType, GameDebugSnapshotCaptureOptions options)
        {
            return string.IsNullOrWhiteSpace(options.ComponentTypeFullName)
                || StringComparer.Ordinal.Equals(componentType.FullName, options.ComponentTypeFullName);
        }

        private static string CreateComponentGraphId(Type type)
        {
            var info = ToDebugType(type);
            return info.AssemblyName + ":" + info.FullName;
        }

        private static DebugTypeInfo ToDebugType(Type type)
        {
            var assemblyName = string.Empty;
            try
            {
                assemblyName = type.Assembly.GetName().Name ?? string.Empty;
            }
            catch
            {
            }

            return new DebugTypeInfo(type.Name, type.FullName ?? type.Name, assemblyName);
        }

        private T ExecuteDebugOperation<T>(Func<T> operation)
        {
            lock (_gate)
            {
                return operation();
            }
        }

        private static ParsedDebugRequest ParseRequest(string request)
        {
            var firstBreak = request.IndexOf('\n', StringComparison.Ordinal);
            if (firstBreak < 0)
            {
                throw new InvalidDataException("The native debug bridge request was malformed.");
            }

            var secondBreak = request.IndexOf('\n', firstBreak + 1);
            if (secondBreak < 0)
            {
                throw new InvalidDataException("The native debug bridge request target was missing.");
            }

            var method = request[..firstBreak].Trim();
            var target = request[(firstBreak + 1)..secondBreak].Trim();
            var body = secondBreak + 1 < request.Length ? request[(secondBreak + 1)..] : string.Empty;
            if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidDataException("The native debug bridge request line was empty.");
            }

            return new ParsedDebugRequest(method.ToUpperInvariant(), ParseTarget(target), body);
        }

        private static DebugHttpTarget ParseTarget(string rawTarget)
        {
            var queryStart = rawTarget.IndexOf('?');
            var path = queryStart >= 0 ? rawTarget[..queryStart] : rawTarget;
            var query = queryStart >= 0 ? rawTarget[queryStart..] : string.Empty;
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

        private static bool TryGetEndpoint(string path, out string endpoint)
        {
            endpoint = string.Empty;
            if (!path.StartsWith(EndpointPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            endpoint = path[EndpointPrefix.Length..];
            if (endpoint.Length == 0)
            {
                endpoint = "/";
                return true;
            }

            return endpoint.StartsWith("/", StringComparison.Ordinal);
        }

        private static string? ExtractJsonString(string body, string key)
        {
            var marker = "\"" + key + "\"";
            var keyIndex = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return null;
            }

            var colon = body.IndexOf(':', keyIndex + marker.Length);
            var quote = colon < 0 ? -1 : body.IndexOf('"', colon + 1);
            if (quote < 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            for (var i = quote + 1; i < body.Length; i++)
            {
                var ch = body[i];
                if (ch == '"')
                {
                    return builder.ToString();
                }

                if (ch == '\\' && i + 1 < body.Length)
                {
                    i++;
                    builder.Append(body[i] switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => body[i],
                    });
                    continue;
                }

                builder.Append(ch);
            }

            return null;
        }

        private static int? ExtractJsonInt(string body, string key)
        {
            var marker = "\"" + key + "\"";
            var keyIndex = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return null;
            }

            var colon = body.IndexOf(':', keyIndex + marker.Length);
            if (colon < 0)
            {
                return null;
            }

            var start = colon + 1;
            while (start < body.Length && char.IsWhiteSpace(body[start]))
            {
                start++;
            }

            var end = start;
            while (end < body.Length && (char.IsDigit(body[end]) || body[end] == '-'))
            {
                end++;
            }

            return end > start && int.TryParse(body[start..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static string JsonResponse(int statusCode, string reasonPhrase, string json)
        {
            return Response(statusCode, reasonPhrase, "application/json; charset=utf-8", json);
        }

        private static string Response(int statusCode, string reasonPhrase, string contentType, string body)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{statusCode}\n{reasonPhrase}\n{contentType}\n{body}");
        }

        private static string HealthJson()
        {
            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",\"capturedAt\":");
            AppendJsonString(builder, FormatTimestamp(DateTimeOffset.UtcNow));
            builder.Append('}');
            return builder.ToString();
        }

        private static string ErrorJson(string message)
        {
            var builder = new StringBuilder();
            builder.Append("{\"message\":");
            AppendJsonString(builder, message);
            builder.Append('}');
            return builder.ToString();
        }

        private static string MutationResultJson(bool succeeded, string message)
        {
            var builder = new StringBuilder();
            builder.Append("{\"succeeded\":").Append(succeeded ? "true" : "false").Append(",\"message\":");
            AppendJsonString(builder, message);
            builder.Append('}');
            return builder.ToString();
        }

        private static string RecordingStateJson()
        {
            return "{\"isRecording\":false,\"startedAt\":null,\"frameCount\":0,\"droppedFrameCount\":0,\"lastDumpName\":null,\"lastDumpPath\":null}";
        }

        private static string ControlResultJson(GameDebugControlResult result)
        {
            var builder = new StringBuilder();
            builder.Append("{\"succeeded\":").Append(result.Succeeded ? "true" : "false").Append(",\"message\":");
            AppendJsonString(builder, result.Message);
            builder.Append(",\"state\":");
            AppendControlState(builder, result.State);
            builder.Append('}');
            return builder.ToString();
        }

        private static string ControlStateJson(GameDebugControlState state)
        {
            var builder = new StringBuilder();
            AppendControlState(builder, state);
            return builder.ToString();
        }

        private static void AppendControlState(StringBuilder builder, GameDebugControlState state)
        {
            builder.Append("{\"isPaused\":").Append(state.IsPaused ? "true" : "false")
                .Append(",\"pendingStepCount\":").Append(state.PendingStepCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"revision\":").Append(state.Revision.ToString(CultureInfo.InvariantCulture))
                .Append(",\"lastCommand\":");
            AppendNullableJsonString(builder, state.LastCommand);
            builder.Append('}');
        }

        private static string SnapshotMessageJson(GameDebugSnapshotMessage message)
        {
            var builder = new StringBuilder(32768);
            builder.Append("{\"frame\":");
            AppendFrame(builder, message.Frame);
            builder.Append(",\"snapshot\":");
            AppendSnapshot(builder, message.Snapshot);
            builder.Append(",\"control\":");
            AppendControlState(builder, message.Control);
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendFrame(StringBuilder builder, GameDebugFrameInfo frame)
        {
            builder.Append("{\"sequence\":").Append(frame.Sequence.ToString(CultureInfo.InvariantCulture))
                .Append(",\"source\":");
            AppendJsonString(builder, frame.Source);
            builder.Append(",\"frame\":").Append(frame.Frame.ToString(CultureInfo.InvariantCulture))
                .Append(",\"capturedAt\":");
            AppendJsonString(builder, FormatTimestamp(frame.CapturedAt));
            builder.Append(",\"metrics\":");
            if (frame.Metrics is null)
            {
                builder.Append("null");
            }
            else
            {
                builder.Append("{\"deltaSeconds\":").Append(FormatDouble(frame.Metrics.DeltaSeconds))
                    .Append(",\"realDeltaSeconds\":").Append(FormatDouble(frame.Metrics.RealDeltaSeconds))
                    .Append(",\"logicMilliseconds\":").Append(FormatDouble(frame.Metrics.LogicMilliseconds))
                    .Append(",\"logicFramesPerSecond\":").Append(FormatDouble(frame.Metrics.LogicFramesPerSecond))
                    .Append('}');
            }
            builder.Append('}');
        }

        private static void AppendSnapshot(StringBuilder builder, GameDebugSnapshot snapshot)
        {
            builder.Append("{\"capturedAt\":");
            AppendJsonString(builder, FormatTimestamp(snapshot.CapturedAt));
            builder.Append(",\"runtimes\":[");
            for (var i = 0; i < snapshot.Runtimes.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendRuntime(builder, snapshot.Runtimes[i]);
            }
            builder.Append("],\"worlds\":[");
            for (var i = 0; i < snapshot.Worlds.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendWorld(builder, snapshot.Worlds[i]);
            }
            builder.Append("]}");
        }

        private static void AppendRuntime(StringBuilder builder, RuntimeContextDebugSnapshot runtime)
        {
            builder.Append("{\"index\":").Append(runtime.Index.ToString(CultureInfo.InvariantCulture))
                .Append(",\"isDisposed\":").Append(runtime.IsDisposed ? "true" : "false")
                .Append(",\"modules\":[");
            for (var i = 0; i < runtime.Modules.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendModule(builder, runtime.Modules[i]);
            }
            builder.Append("],\"procedureModules\":[");
            for (var i = 0; i < runtime.ProcedureModules.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendProcedureModule(builder, runtime.ProcedureModules[i]);
            }
            builder.Append("]}");
        }

        private static void AppendModule(StringBuilder builder, ModuleDebugSnapshot module)
        {
            builder.Append("{\"type\":");
            AppendTypeInfo(builder, module.Type);
            builder.Append(",\"priority\":").Append(module.Priority.ToString(CultureInfo.InvariantCulture))
                .Append(",\"isUpdateModule\":").Append(module.IsUpdateModule ? "true" : "false")
                .Append('}');
        }

        private static void AppendProcedureModule(StringBuilder builder, ProcedureModuleDebugSnapshot module)
        {
            builder.Append("{\"type\":");
            AppendTypeInfo(builder, module.Type);
            builder.Append(",\"isInitialized\":").Append(module.IsInitialized ? "true" : "false")
                .Append(",\"currentProcedure\":");
            AppendNullableJsonString(builder, module.CurrentProcedure);
            builder.Append(",\"currentProcedureTime\":").Append(FormatDouble(module.CurrentProcedureTime))
                .Append(",\"procedures\":[");
            for (var i = 0; i < module.Procedures.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"type\":");
                AppendTypeInfo(builder, module.Procedures[i].Type);
                builder.Append(",\"isCurrent\":").Append(module.Procedures[i].IsCurrent ? "true" : "false").Append('}');
            }
            builder.Append("]}");
        }

        private static void AppendWorld(StringBuilder builder, WorldDebugSnapshot world)
        {
            builder.Append("{\"name\":");
            AppendJsonString(builder, world.Name);
            builder.Append(",\"sceneCount\":").Append(world.SceneCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"scenes\":[");
            for (var i = 0; i < world.Scenes.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendScene(builder, world.Scenes[i]);
            }
            builder.Append("]}");
        }

        private static void AppendScene(StringBuilder builder, SceneDebugSnapshot scene)
        {
            builder.Append("{\"name\":");
            AppendJsonString(builder, scene.Name);
            builder.Append(",\"entityCount\":").Append(scene.EntityCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"systems\":[");
            for (var i = 0; i < scene.Systems.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendSystem(builder, scene.Systems[i]);
            }
            builder.Append("],\"componentStores\":[");
            for (var i = 0; i < scene.ComponentStores.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendComponentStore(builder, scene.ComponentStores[i]);
            }
            builder.Append("],\"entities\":[");
            for (var i = 0; i < scene.Entities.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendEntity(builder, scene.Entities[i]);
            }
            builder.Append("]}");
        }

        private static void AppendSystem(StringBuilder builder, SystemDebugSnapshot system)
        {
            builder.Append("{\"type\":");
            AppendTypeInfo(builder, system.Type);
            builder.Append(",\"order\":").Append(system.Order.ToString(CultureInfo.InvariantCulture))
                .Append(",\"enabled\":").Append(system.Enabled ? "true" : "false")
                .Append('}');
        }

        private static void AppendComponentStore(StringBuilder builder, ComponentStoreDebugSnapshot store)
        {
            builder.Append("{\"type\":");
            AppendTypeInfo(builder, store.Type);
            builder.Append(",\"count\":").Append(store.Count.ToString(CultureInfo.InvariantCulture))
                .Append(",\"entityIds\":[");
            for (var i = 0; i < store.EntityIds.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append(store.EntityIds[i].ToString(CultureInfo.InvariantCulture));
            }
            builder.Append("]}");
        }

        private static void AppendEntity(StringBuilder builder, EntityDebugSnapshot entity)
        {
            builder.Append("{\"id\":").Append(entity.Id.ToString(CultureInfo.InvariantCulture))
                .Append(",\"version\":").Append(entity.Version.ToString(CultureInfo.InvariantCulture))
                .Append(",\"components\":[");
            for (var i = 0; i < entity.Components.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendComponent(builder, entity.Components[i]);
            }
            builder.Append("],\"skills\":[],\"buffs\":[]}");
        }

        private static void AppendComponent(StringBuilder builder, ComponentDebugSnapshot component)
        {
            builder.Append("{\"type\":");
            AppendTypeInfo(builder, component.Type);
            builder.Append(",\"value\":");
            AppendComponentValue(builder, component.Value);
            builder.Append(",\"graph\":");
            AppendGraph(builder, component.Graph);
            builder.Append('}');
        }

        private static void AppendComponentValue(StringBuilder builder, ComponentValueDebugSnapshot value)
        {
            builder.Append("{\"format\":");
            AppendJsonString(builder, value.Format);
            builder.Append(",\"payload\":");
            AppendNullableJsonString(builder, value.Payload);
            builder.Append(",\"error\":");
            AppendNullableJsonString(builder, value.Error);
            builder.Append(",\"structured\":");
            if (value.Structured is null)
            {
                builder.Append("null");
            }
            else
            {
                AppendComponentNode(builder, value.Structured);
            }
            builder.Append('}');
        }

        private static void AppendComponentNode(StringBuilder builder, ComponentValueDebugNode node)
        {
            builder.Append("{\"kind\":");
            AppendJsonString(builder, node.Kind);
            builder.Append(",\"name\":");
            AppendNullableJsonString(builder, node.Name);
            builder.Append(",\"type\":");
            AppendTypeInfo(builder, node.Type);
            builder.Append(",\"editable\":").Append(node.Editable ? "true" : "false")
                .Append(",\"value\":");
            AppendNullableJsonString(builder, node.Value);
            builder.Append(",\"children\":[");
            for (var i = 0; i < node.Children.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendComponentNode(builder, node.Children[i]);
            }
            builder.Append("],\"options\":[");
            for (var i = 0; i < node.Options.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                AppendJsonString(builder, node.Options[i]);
            }
            builder.Append("],\"elementType\":");
            if (node.ElementType is null)
            {
                builder.Append("null");
            }
            else
            {
                AppendTypeInfo(builder, node.ElementType);
            }
            builder.Append(",\"elementTemplate\":");
            if (node.ElementTemplate is null)
            {
                builder.Append("null");
            }
            else
            {
                AppendComponentNode(builder, node.ElementTemplate);
            }
            builder.Append(",\"error\":");
            AppendNullableJsonString(builder, node.Error);
            builder.Append('}');
        }

        private static void AppendGraph(StringBuilder builder, ComponentGraphDebugSnapshot graph)
        {
            builder.Append("{\"id\":");
            AppendJsonString(builder, graph.Id);
            builder.Append(",\"parentId\":");
            AppendNullableJsonString(builder, graph.ParentId);
            builder.Append(",\"parentType\":");
            if (graph.ParentType is null)
            {
                builder.Append("null");
            }
            else
            {
                AppendTypeInfo(builder, graph.ParentType);
            }
            builder.Append(",\"group\":");
            AppendNullableJsonString(builder, graph.Group);
            builder.Append(",\"order\":").Append(graph.Order.ToString(CultureInfo.InvariantCulture)).Append('}');
        }

        private static void AppendTypeInfo(StringBuilder builder, DebugTypeInfo type)
        {
            builder.Append("{\"name\":");
            AppendJsonString(builder, type.Name);
            builder.Append(",\"fullName\":");
            AppendJsonString(builder, type.FullName);
            builder.Append(",\"assemblyName\":");
            AppendJsonString(builder, type.AssemblyName);
            builder.Append('}');
        }

        private static void AppendNullableJsonString(StringBuilder builder, string? value)
        {
            if (value is null)
            {
                builder.Append("null");
                return;
            }

            AppendJsonString(builder, value);
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (ch < ' ')
                        {
                            builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatTimestamp(DateTimeOffset value)
        {
            _ = value;
            return "1970-01-01T00:00:00.000Z";
        }

        private static GameDebugSnapshotCaptureOptions CreateSnapshotCaptureOptions(
            IReadOnlyDictionary<string, string> query)
        {
            return new GameDebugSnapshotCaptureOptions
            {
                WorldName = GetString(query, "world") ?? GetString(query, "worldName"),
                SceneName = GetString(query, "scene") ?? GetString(query, "sceneName"),
                EntityId = GetInt(query, "entityId"),
                ComponentTypeFullName = GetString(query, "componentTypeFullName") ?? GetString(query, "component"),
                ComponentAssemblyName = GetString(query, "componentAssemblyName") ?? GetString(query, "componentAssembly"),
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

        private static string? GetString(IReadOnlyDictionary<string, string> query, string key)
        {
            return query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        }

        private static int? GetInt(IReadOnlyDictionary<string, string> query, string key)
        {
            return query.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : null;
        }

        private static int? NormalizeLimit(int? value)
        {
            return value is > 0 ? value : null;
        }

        private static bool? GetBool(IReadOnlyDictionary<string, string> query, string key)
        {
            if (!query.TryGetValue(key, out var value))
            {
                return null;
            }

            if (bool.TryParse(value, out var result))
            {
                return result;
            }

            return value.Trim() switch
            {
                "1" => true,
                "0" => false,
                _ => null,
            };
        }

        private static bool IsGet(ParsedDebugRequest request, string endpoint, string expectedEndpoint)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(request.Method, "GET")
                && StringComparer.Ordinal.Equals(endpoint, expectedEndpoint);
        }

        private static bool IsPost(ParsedDebugRequest request, string endpoint, string expectedEndpoint)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(request.Method, "POST")
                && StringComparer.Ordinal.Equals(endpoint, expectedEndpoint);
        }

        private sealed record ParsedDebugRequest(string Method, DebugHttpTarget Target, string Body);

        private sealed record DebugHttpTarget(string Path, IReadOnlyDictionary<string, string> Query);

        private sealed class SynchronizedSnapshotProvider : IGameDebugSnapshotProvider
        {
            private readonly IGameDebugSnapshotProvider _inner;
            private readonly object _gate;

            public SynchronizedSnapshotProvider(IGameDebugSnapshotProvider inner, object gate)
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
    }
}
