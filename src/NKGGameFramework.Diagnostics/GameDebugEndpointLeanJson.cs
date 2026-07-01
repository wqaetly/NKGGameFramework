using System.Globalization;
using System.Text;

namespace NKGGameFramework.Diagnostics;

internal static class GameDebugEndpointLeanJson
{
    public static byte[] SerializeError(string message)
    {
        var builder = new StringBuilder();
        builder.Append("{\"message\":");
        AppendJsonString(builder, message);
        builder.Append('}');
        return ToUtf8(builder);
    }

    public static byte[] SerializeHealth(DateTimeOffset capturedAt)
    {
        var builder = new StringBuilder();
        builder.Append("{\"status\":\"ok\",\"capturedAt\":");
        AppendJsonString(builder, FormatTimestamp(capturedAt));
        builder.Append('}');
        return ToUtf8(builder);
    }

    public static byte[] Serialize(GameDebugSnapshotMessage value)
    {
        var builder = new StringBuilder(32768);
        builder.Append("{\"frame\":");
        AppendFrame(builder, value.Frame);
        builder.Append(",\"snapshot\":");
        AppendSnapshot(builder, value.Snapshot);
        builder.Append(",\"control\":");
        AppendControlState(builder, value.Control);
        builder.Append('}');
        return ToUtf8(builder);
    }

    public static byte[] Serialize(GameDebugControlState value)
    {
        var builder = new StringBuilder();
        AppendControlState(builder, value);
        return ToUtf8(builder);
    }

    public static byte[] Serialize(GameDebugControlResult value)
    {
        var builder = new StringBuilder();
        builder.Append("{\"succeeded\":").Append(value.Succeeded ? "true" : "false")
            .Append(",\"message\":");
        AppendJsonString(builder, value.Message);
        builder.Append(",\"state\":");
        AppendControlState(builder, value.State);
        builder.Append('}');
        return ToUtf8(builder);
    }

    public static byte[] Serialize(GameDebugMutationResult value)
    {
        var builder = new StringBuilder();
        builder.Append("{\"succeeded\":").Append(value.Succeeded ? "true" : "false")
            .Append(",\"message\":");
        AppendJsonString(builder, value.Message);
        builder.Append('}');
        return ToUtf8(builder);
    }

    public static byte[] Serialize(GameDebugDumpRecordingState value)
    {
        var builder = new StringBuilder();
        AppendRecordingState(builder, value);
        return ToUtf8(builder);
    }

    public static byte[] Serialize(GameDebugDumpRecordingResult value)
    {
        var builder = new StringBuilder();
        builder.Append("{\"succeeded\":").Append(value.Succeeded ? "true" : "false")
            .Append(",\"message\":");
        AppendJsonString(builder, value.Message);
        builder.Append(",\"state\":");
        AppendRecordingState(builder, value.State);
        builder.Append('}');
        return ToUtf8(builder);
    }

    public static byte[] Serialize(GameDebugDumpPlaybackManifest value)
    {
        var builder = new StringBuilder();
        builder.Append("{\"id\":");
        AppendJsonString(builder, value.Id);
        builder.Append(",\"format\":");
        AppendJsonString(builder, value.Format);
        builder.Append(",\"version\":").Append(value.Version.ToString(CultureInfo.InvariantCulture))
            .Append(",\"name\":");
        AppendJsonString(builder, value.Name);
        builder.Append(",\"createdAt\":");
        AppendJsonString(builder, FormatTimestamp(value.CreatedAt));
        builder.Append(",\"startedAt\":");
        AppendJsonString(builder, FormatTimestamp(value.StartedAt));
        builder.Append(",\"endedAt\":");
        AppendJsonString(builder, FormatTimestamp(value.EndedAt));
        builder.Append(",\"frames\":[");
        for (var i = 0; i < value.Frames.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"index\":").Append(value.Frames[i].Index.ToString(CultureInfo.InvariantCulture))
                .Append(",\"frame\":");
            AppendFrame(builder, value.Frames[i].Frame);
            builder.Append('}');
        }
        builder.Append("]}");
        return ToUtf8(builder);
    }

    public static byte[] Serialize(GameDebugDumpAnalysisReport value)
    {
        var builder = new StringBuilder();
        builder.Append("{\"format\":");
        AppendJsonString(builder, value.Format);
        builder.Append(",\"version\":").Append(value.Version.ToString(CultureInfo.InvariantCulture))
            .Append(",\"name\":");
        AppendJsonString(builder, value.Name);
        builder.Append(",\"frameCount\":").Append(value.FrameCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"serializedBytes\":").Append(value.SerializedBytes.ToString(CultureInfo.InvariantCulture))
            .Append(",\"total\":");
        AppendSize(builder, value.Total);
        builder.Append(",\"types\":");
        AppendAnalysisEntries(builder, value.Types);
        builder.Append(",\"fields\":");
        AppendAnalysisEntries(builder, value.Fields);
        builder.Append(",\"components\":");
        AppendAnalysisEntries(builder, value.Components);
        builder.Append(",\"entities\":");
        AppendAnalysisEntries(builder, value.Entities);
        builder.Append(",\"scenes\":");
        AppendAnalysisEntries(builder, value.Scenes);
        builder.Append(",\"recordingMetrics\":");
        AppendRecordingMetrics(builder, value.RecordingMetrics);
        builder.Append('}');
        return ToUtf8(builder);
    }

    public static byte[] Serialize(ComponentDebugSnapshot value)
    {
        var builder = new StringBuilder();
        AppendComponent(builder, value);
        return ToUtf8(builder);
    }

    public static GameDebugControlRequest DeserializeControlRequest(byte[] body)
    {
        var json = GetBodyText(body);
        return new GameDebugControlRequest(
            ExtractJsonString(json, "command") ?? string.Empty,
            ExtractJsonInt(json, "stepCount"));
    }

    public static GameDebugMutationRequest DeserializeMutationRequest(byte[] body)
    {
        var json = GetBodyText(body);
        var valueJson = ExtractJsonObject(json, "value") ?? "{}";
        var structuredJson = ExtractJsonObject(valueJson, "structured");
        return new GameDebugMutationRequest(
            ExtractJsonString(json, "worldName") ?? string.Empty,
            ExtractJsonString(json, "sceneName") ?? string.Empty,
            ExtractJsonInt(json, "entityId") ?? 0,
            ExtractJsonInt(json, "entityVersion"),
            ExtractJsonString(json, "componentTypeFullName") ?? string.Empty,
            ExtractJsonString(json, "componentAssemblyName") ?? string.Empty,
            new ComponentValueDebugSnapshot(
                ExtractJsonString(valueJson, "format") ?? string.Empty,
                ExtractJsonString(valueJson, "payload"),
                ExtractJsonString(valueJson, "error"),
                structuredJson is null ? null : DeserializeComponentNode(structuredJson)));
    }

    public static GameDebugDumpRecordingRequest DeserializeRecordingRequest(byte[] body)
    {
        var json = GetBodyText(body);
        return new GameDebugDumpRecordingRequest(
            ExtractJsonString(json, "command") ?? string.Empty,
            ExtractJsonString(json, "name"),
            ExtractJsonString(json, "dumpDirectory"));
    }

    public static GameDebugDumpPlaybackOpenRequest DeserializePlaybackOpenRequest(byte[] body)
    {
        var json = GetBodyText(body);
        return new GameDebugDumpPlaybackOpenRequest(ExtractJsonString(json, "path"));
    }

    private static string GetBodyText(byte[] body)
    {
        if (body.Length == 0)
        {
            throw new InvalidDataException("The debug request body was empty.");
        }

        return Encoding.UTF8.GetString(body);
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
            builder.Append(",\"isCurrent\":").Append(module.Procedures[i].IsCurrent ? "true" : "false")
                .Append('}');
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
        builder.Append("],\"skills\":[");
        for (var i = 0; i < entity.Skills.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            AppendSkill(builder, entity.Skills[i]);
        }
        builder.Append("],\"buffs\":[");
        for (var i = 0; i < entity.Buffs.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            AppendBuff(builder, entity.Buffs[i]);
        }
        builder.Append("]}");
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

    private static void AppendSkill(StringBuilder builder, SkillDebugSnapshot skill)
    {
        builder.Append("{\"id\":");
        AppendJsonString(builder, skill.Id);
        builder.Append(",\"displayName\":");
        AppendNullableJsonString(builder, skill.DisplayName);
        builder.Append(",\"level\":").Append(skill.Level.ToString(CultureInfo.InvariantCulture))
            .Append(",\"kind\":");
        AppendJsonString(builder, skill.Kind);
        builder.Append(",\"releaseMode\":");
        AppendJsonString(builder, skill.ReleaseMode);
        builder.Append(",\"costKind\":");
        AppendJsonString(builder, skill.CostKind);
        builder.Append(",\"cost\":").Append(FormatDouble(skill.Cost))
            .Append(",\"cooldownSeconds\":").Append(FormatDouble(skill.CooldownSeconds))
            .Append(",\"cooldownRemainingSeconds\":").Append(FormatDouble(skill.CooldownRemainingSeconds))
            .Append(",\"isCoolingDown\":").Append(skill.IsCoolingDown ? "true" : "false")
            .Append(",\"tags\":");
        AppendStringArray(builder, skill.Tags);
        builder.Append(",\"resourceLocations\":");
        AppendStringArray(builder, skill.ResourceLocations);
        builder.Append(",\"effectKeys\":");
        AppendStringArray(builder, skill.EffectKeys);
        builder.Append('}');
    }

    private static void AppendBuff(StringBuilder builder, BuffDebugSnapshot buff)
    {
        builder.Append("{\"id\":");
        AppendJsonString(builder, buff.Id);
        builder.Append(",\"displayName\":");
        AppendNullableJsonString(builder, buff.DisplayName);
        builder.Append(",\"level\":").Append(buff.Level.ToString(CultureInfo.InvariantCulture))
            .Append(",\"stacks\":").Append(buff.Stacks.ToString(CultureInfo.InvariantCulture))
            .Append(",\"state\":");
        AppendJsonString(builder, buff.State);
        builder.Append(",\"kind\":");
        AppendJsonString(builder, buff.Kind);
        builder.Append(",\"effectKey\":");
        AppendJsonString(builder, buff.EffectKey);
        builder.Append(",\"remainingDurationSeconds\":");
        if (buff.RemainingDurationSeconds is { } remaining)
        {
            builder.Append(FormatDouble(remaining));
        }
        else
        {
            builder.Append("null");
        }
        builder.Append(",\"source\":");
        AppendEntityRef(builder, buff.Source);
        builder.Append(",\"target\":");
        AppendEntityRef(builder, buff.Target);
        builder.Append(",\"tags\":");
        AppendStringArray(builder, buff.Tags);
        builder.Append('}');
    }

    private static void AppendEntityRef(StringBuilder builder, EntityRefDebugSnapshot entityRef)
    {
        builder.Append("{\"id\":").Append(entityRef.Id.ToString(CultureInfo.InvariantCulture))
            .Append(",\"version\":").Append(entityRef.Version.ToString(CultureInfo.InvariantCulture))
            .Append(",\"isAlive\":").Append(entityRef.IsAlive ? "true" : "false")
            .Append('}');
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

    private static void AppendControlState(StringBuilder builder, GameDebugControlState state)
    {
        builder.Append("{\"isPaused\":").Append(state.IsPaused ? "true" : "false")
            .Append(",\"pendingStepCount\":").Append(state.PendingStepCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"revision\":").Append(state.Revision.ToString(CultureInfo.InvariantCulture))
            .Append(",\"lastCommand\":");
        AppendNullableJsonString(builder, state.LastCommand);
        builder.Append('}');
    }

    private static void AppendRecordingState(StringBuilder builder, GameDebugDumpRecordingState state)
    {
        builder.Append("{\"isRecording\":").Append(state.IsRecording ? "true" : "false")
            .Append(",\"startedAt\":");
        if (state.StartedAt is { } startedAt)
        {
            AppendJsonString(builder, FormatTimestamp(startedAt));
        }
        else
        {
            builder.Append("null");
        }
        builder.Append(",\"frameCount\":").Append(state.FrameCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"lastDumpName\":");
        AppendNullableJsonString(builder, state.LastDumpName);
        builder.Append(",\"lastDumpPath\":");
        AppendNullableJsonString(builder, state.LastDumpPath);
        builder.Append(",\"isFinalizing\":").Append(state.IsFinalizing ? "true" : "false")
            .Append(",\"lastDumpError\":");
        AppendNullableJsonString(builder, state.LastDumpError);
        builder.Append(",\"metrics\":");
        AppendRecordingMetrics(builder, state.Metrics);
        builder.Append('}');
    }

    private static void AppendRecordingMetrics(StringBuilder builder, GameDebugDumpRecordingMetrics? metrics)
    {
        if (metrics is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("{\"publishedFrameCount\":").Append(metrics.PublishedFrameCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"capturedFrameCount\":").Append(metrics.CapturedFrameCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"pendingCaptureCount\":").Append(metrics.PendingCaptureCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"lastFrameCallbackMilliseconds\":").Append(metrics.LastFrameCallbackMilliseconds.ToString("G17", CultureInfo.InvariantCulture))
            .Append(",\"maxFrameCallbackMilliseconds\":").Append(metrics.MaxFrameCallbackMilliseconds.ToString("G17", CultureInfo.InvariantCulture))
            .Append(",\"averageFrameCallbackMilliseconds\":").Append(metrics.AverageFrameCallbackMilliseconds.ToString("G17", CultureInfo.InvariantCulture))
            .Append(",\"lastCaptureMilliseconds\":").Append(metrics.LastCaptureMilliseconds.ToString("G17", CultureInfo.InvariantCulture))
            .Append(",\"maxCaptureMilliseconds\":").Append(metrics.MaxCaptureMilliseconds.ToString("G17", CultureInfo.InvariantCulture))
            .Append(",\"averageCaptureMilliseconds\":").Append(metrics.AverageCaptureMilliseconds.ToString("G17", CultureInfo.InvariantCulture))
            .Append(",\"lastCapturedStoreCount\":").Append(metrics.LastCapturedStoreCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"lastCapturedEntityRowCount\":").Append(metrics.LastCapturedEntityRowCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"maxCapturedStoreCount\":").Append(metrics.MaxCapturedStoreCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"maxCapturedEntityRowCount\":").Append(metrics.MaxCapturedEntityRowCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"totalCapturedStoreCount\":").Append(metrics.TotalCapturedStoreCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"totalCapturedEntityRowCount\":").Append(metrics.TotalCapturedEntityRowCount.ToString(CultureInfo.InvariantCulture))
            .Append(",\"lastCaptureAllocatedBytes\":");
        AppendNullableLong(builder, metrics.LastCaptureAllocatedBytes);
        builder.Append(",\"totalCaptureAllocatedBytes\":");
        AppendNullableLong(builder, metrics.TotalCaptureAllocatedBytes);
        builder.Append('}');
    }

    private static void AppendNullableLong(StringBuilder builder, long? value)
    {
        if (value is { } number)
        {
            builder.Append(number.ToString(CultureInfo.InvariantCulture));
            return;
        }

        builder.Append("null");
    }

    private static void AppendAnalysisEntries(StringBuilder builder, IReadOnlyList<GameDebugDumpAnalysisEntry> entries)
    {
        builder.Append('[');
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"key\":");
            AppendJsonString(builder, entries[i].Key);
            builder.Append(",\"displayName\":");
            AppendNullableJsonString(builder, entries[i].DisplayName);
            builder.Append(",\"size\":");
            AppendSize(builder, entries[i].Size);
            builder.Append(",\"count\":").Append(entries[i].Count.ToString(CultureInfo.InvariantCulture))
                .Append('}');
        }
        builder.Append(']');
    }

    private static void AppendSize(StringBuilder builder, GameDebugDumpAnalysisSizeBreakdown size)
    {
        builder.Append("{\"totalBytes\":").Append(size.TotalBytes.ToString(CultureInfo.InvariantCulture))
            .Append(",\"payloadBytes\":").Append(size.PayloadBytes.ToString(CultureInfo.InvariantCulture))
            .Append(",\"structuredBytes\":").Append(size.StructuredBytes.ToString(CultureInfo.InvariantCulture))
            .Append('}');
    }

    private static void AppendStringArray(StringBuilder builder, IReadOnlyList<string> values)
    {
        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            AppendJsonString(builder, values[i]);
        }
        builder.Append(']');
    }

    private static string? ExtractJsonObject(string body, string key)
    {
        var start = FindJsonValueStart(body, key);
        if (start >= 0 && start < body.Length && body[start] != '{')
        {
            return null;
        }
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < body.Length; i++)
        {
            var ch = body[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return body[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static string? ExtractJsonArray(string body, string key)
    {
        var start = FindJsonValueStart(body, key);
        if (start >= 0 && start < body.Length && body[start] != '[')
        {
            return null;
        }
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < body.Length; i++)
        {
            var ch = body[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return body[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static ComponentValueDebugNode DeserializeComponentNode(string json)
    {
        var type = DeserializeTypeInfo(ExtractJsonObject(json, "type"));
        var elementTypeJson = ExtractJsonObject(json, "elementType");
        var elementTemplateJson = ExtractJsonObject(json, "elementTemplate");
        return new ComponentValueDebugNode
        {
            Kind = ExtractJsonString(json, "kind") ?? "value",
            Name = ExtractJsonString(json, "name"),
            Type = type,
            Editable = ExtractJsonBool(json, "editable") ?? false,
            Value = ExtractJsonString(json, "value"),
            Children = DeserializeComponentNodes(ExtractJsonArray(json, "children")),
            Options = DeserializeStringArray(ExtractJsonArray(json, "options")),
            ElementType = elementTypeJson is null ? null : DeserializeTypeInfo(elementTypeJson),
            ElementTemplate = elementTemplateJson is null ? null : DeserializeComponentNode(elementTemplateJson),
            Error = ExtractJsonString(json, "error"),
        };
    }

    private static DebugTypeInfo DeserializeTypeInfo(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new DebugTypeInfo(string.Empty, string.Empty, string.Empty);
        }

        return new DebugTypeInfo(
            ExtractJsonString(json, "name") ?? string.Empty,
            ExtractJsonString(json, "fullName") ?? string.Empty,
            ExtractJsonString(json, "assemblyName") ?? string.Empty);
    }

    private static IReadOnlyList<ComponentValueDebugNode> DeserializeComponentNodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ComponentValueDebugNode>();
        }

        var objects = SplitTopLevelValues(json, '{', '}');
        var nodes = new List<ComponentValueDebugNode>(objects.Count);
        foreach (var item in objects)
        {
            nodes.Add(DeserializeComponentNode(item));
        }

        return nodes;
    }

    private static IReadOnlyList<string> DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        return SplitTopLevelValues(json, '"', '"')
            .Select(static value => ExtractJsonString("{\"v\":" + value + "}", "v") ?? string.Empty)
            .ToArray();
    }

    private static List<string> SplitTopLevelValues(string json, char startChar, char endChar)
    {
        var values = new List<string>();
        var inString = false;
        var escaped = false;
        var objectDepth = 0;
        var arrayDepth = 0;
        var start = -1;

        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                    if (startChar == '"' && start >= 0 && objectDepth == 0 && arrayDepth == 1)
                    {
                        values.Add(json[start..(i + 1)]);
                        start = -1;
                    }
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                if (startChar == '"' && start < 0 && objectDepth == 0 && arrayDepth == 1)
                {
                    start = i;
                }
                continue;
            }

            if (ch == '{')
            {
                if (startChar == '{' && objectDepth == 0 && arrayDepth == 1)
                {
                    start = i;
                }
                objectDepth++;
            }
            else if (ch == '}')
            {
                objectDepth--;
                if (endChar == '}' && objectDepth == 0 && start >= 0)
                {
                    values.Add(json[start..(i + 1)]);
                    start = -1;
                }
            }
            else if (ch == '[')
            {
                arrayDepth++;
            }
            else if (ch == ']')
            {
                arrayDepth--;
            }
        }

        return values;
    }

    private static string? ExtractJsonString(string body, string key)
    {
        var cursor = FindJsonValueStart(body, key);
        if (cursor < 0)
        {
            return null;
        }
        if (cursor + 4 <= body.Length &&
            string.Compare(body, cursor, "null", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return null;
        }

        if (cursor >= body.Length || body[cursor] != '"')
        {
            return null;
        }

        var builder = new StringBuilder();
        for (var i = cursor + 1; i < body.Length; i++)
        {
            var ch = body[i];
            if (ch == '"')
            {
                return builder.ToString();
            }

            if (ch == '\\' && i + 1 < body.Length)
            {
                i++;
                var escaped = body[i];
                if (escaped == 'u' && i + 4 < body.Length)
                {
                    var hex = body.Substring(i + 1, 4);
                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    {
                        builder.Append((char)code);
                        i += 4;
                        continue;
                    }
                }

                builder.Append(escaped switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped,
                });
                continue;
            }

            builder.Append(ch);
        }

        return null;
    }

    private static int? ExtractJsonInt(string body, string key)
    {
        var start = FindJsonValueStart(body, key);
        if (start < 0)
        {
            return null;
        }
        if (start + 4 <= body.Length &&
            string.Compare(body, start, "null", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return null;
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

    private static bool? ExtractJsonBool(string body, string key)
    {
        var start = FindJsonValueStart(body, key);
        if (start < 0)
        {
            return null;
        }
        if (start + 4 <= body.Length &&
            string.Compare(body, start, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return true;
        }

        if (start + 5 <= body.Length &&
            string.Compare(body, start, "false", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return false;
        }

        return null;
    }

    private static int FindJsonValueStart(string body, string key)
    {
        var objectDepth = 0;
        var arrayDepth = 0;
        var inString = false;
        var escaped = false;
        var stringStart = -1;

        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                    if (objectDepth == 1 && arrayDepth == 0 && stringStart >= 0)
                    {
                        var candidate = body[(stringStart + 1)..i];
                        if (StringComparer.OrdinalIgnoreCase.Equals(candidate, key))
                        {
                            var cursor = i + 1;
                            while (cursor < body.Length && char.IsWhiteSpace(body[cursor]))
                            {
                                cursor++;
                            }

                            if (cursor < body.Length && body[cursor] == ':')
                            {
                                cursor++;
                                while (cursor < body.Length && char.IsWhiteSpace(body[cursor]))
                                {
                                    cursor++;
                                }

                                return cursor;
                            }
                        }
                    }

                    stringStart = -1;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                stringStart = i;
                continue;
            }

            if (ch == '{')
            {
                objectDepth++;
            }
            else if (ch == '}')
            {
                objectDepth--;
            }
            else if (ch == '[')
            {
                arrayDepth++;
            }
            else if (ch == ']')
            {
                arrayDepth--;
            }
        }

        return -1;
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
        return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    private static byte[] ToUtf8(StringBuilder builder)
    {
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
