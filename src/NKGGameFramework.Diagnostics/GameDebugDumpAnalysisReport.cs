using System.Text;
using System.Text.Json;

namespace NKGGameFramework.Diagnostics;

public sealed record GameDebugDumpAnalysisReport(
    string Format,
    int Version,
    string Name,
    int FrameCount,
    long SerializedBytes,
    GameDebugDumpAnalysisSizeBreakdown Total,
    IReadOnlyList<GameDebugDumpAnalysisEntry> Types,
    IReadOnlyList<GameDebugDumpAnalysisEntry> Fields,
    IReadOnlyList<GameDebugDumpAnalysisEntry> Components,
    IReadOnlyList<GameDebugDumpAnalysisEntry> Entities,
    IReadOnlyList<GameDebugDumpAnalysisEntry> Scenes);

public sealed record GameDebugDumpAnalysisSizeBreakdown(
    long TotalBytes,
    long PayloadBytes,
    long StructuredBytes);

public sealed record GameDebugDumpAnalysisEntry(
    string Key,
    string? DisplayName,
    GameDebugDumpAnalysisSizeBreakdown Size,
    int Count);

public static class GameDebugDumpAnalyzer
{
    public static GameDebugDumpAnalysisReport Analyze(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Analyze(GameDebugDumpFile.Deserialize(payload), payload.Length);
    }

    public static GameDebugDumpAnalysisReport Analyze(GameDebugDumpDocument dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        return Analyze(dump, GameDebugDumpFile.Serialize(dump).Length);
    }

    public static string ToJson(GameDebugDumpAnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, GameDebugJson.Options);
    }

    public static string ToTable(GameDebugDumpAnalysisReport report, int limit = 10)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "The dump analysis table limit must be positive.");
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Dump: {report.Name}");
        builder.AppendLine($"Frames: {report.FrameCount}");
        builder.AppendLine($"Serialized bytes: {report.SerializedBytes}");
        builder.AppendLine($"Total accounted bytes: {report.Total.TotalBytes} (payload {report.Total.PayloadBytes}, structured {report.Total.StructuredBytes})");
        AppendSection(builder, "Types", report.Types, limit);
        AppendSection(builder, "Fields", report.Fields, limit);
        AppendSection(builder, "Components", report.Components, limit);
        AppendSection(builder, "Entities", report.Entities, limit);
        AppendSection(builder, "Scenes", report.Scenes, limit);
        return builder.ToString();
    }

    private static GameDebugDumpAnalysisReport Analyze(GameDebugDumpDocument dump, long serializedBytes)
    {
        return dump.BlockFrames is { Count: > 0 }
            ? AnalyzeBlocks(dump, serializedBytes)
            : AnalyzeSnapshotFrames(dump, serializedBytes);
    }

    private static GameDebugDumpAnalysisReport AnalyzeSnapshotFrames(GameDebugDumpDocument dump, long serializedBytes)
    {
        var serializer = new OdinGameDebugComponentValueSerializer();
        var structuredOptions = new GameDebugStructuredComponentValueCaptureOptions
        {
            MaxCollectionItems = 64,
            CaptureElementTemplate = false,
        };
        var types = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var fields = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var components = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var entities = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var scenes = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var total = new Accumulator("total", "Total");

        foreach (var frame in dump.Frames)
        {
            foreach (var world in frame.Snapshot.Worlds)
            {
                foreach (var scene in world.Scenes)
                {
                    var sceneKey = $"{world.Name}/{scene.Name}";
                    foreach (var entity in scene.Entities)
                    {
                        var entityKey = $"{sceneKey}/entity/{entity.Id}";
                        foreach (var component in entity.Components)
                        {
                            var materializedComponent = GameDebugComponentValueMaterializer.MaterializeStructured(
                                component,
                                serializer,
                                structuredOptions);
                            var componentBytes = EstimateComponentBytes(materializedComponent);
                            var payloadBytes = GetStringBytes(materializedComponent.Value.Payload);
                            var structuredBytes = materializedComponent.Value.Structured is null
                                ? 0
                                : EstimateStructuredBytes(materializedComponent.Value.Structured);
                            var typeKey = CreateTypeKey(materializedComponent.Type);
                            var componentKey = $"{entityKey}/component/{typeKey}";

                            Add(types, typeKey, materializedComponent.Type.Name, componentBytes, payloadBytes, structuredBytes);
                            Add(components, componentKey, materializedComponent.Type.Name, componentBytes, payloadBytes, structuredBytes);
                            Add(entities, entityKey, $"Entity {entity.Id}", componentBytes, payloadBytes, structuredBytes);
                            Add(scenes, sceneKey, scene.Name, componentBytes, payloadBytes, structuredBytes);
                            total.Add(componentBytes, payloadBytes, structuredBytes);

                            if (materializedComponent.Value.Structured is not null)
                            {
                                if (materializedComponent.Value.Structured.Children.Count == 0)
                                {
                                    AddFields(fields, typeKey, materializedComponent.Type.Name, materializedComponent.Value.Structured, string.Empty);
                                }
                                else
                                {
                                    foreach (var child in materializedComponent.Value.Structured.Children)
                                    {
                                        AddFields(fields, typeKey, materializedComponent.Type.Name, child, string.Empty);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return new GameDebugDumpAnalysisReport(
            dump.Format,
            dump.Version,
            dump.Name,
            dump.Frames.Count,
            serializedBytes,
            total.ToBreakdown(),
            Sort(types),
            Sort(fields),
            Sort(components),
            Sort(entities),
            Sort(scenes));
    }

    private static GameDebugDumpAnalysisReport AnalyzeBlocks(GameDebugDumpDocument dump, long serializedBytes)
    {
        var serializer = new OdinGameDebugComponentValueSerializer();
        var structuredOptions = new GameDebugStructuredComponentValueCaptureOptions
        {
            MaxCollectionItems = 64,
            CaptureElementTemplate = false,
        };
        var types = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var fields = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var components = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var entities = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var scenes = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var total = new Accumulator("total", "Total");

        foreach (var frame in dump.BlockFrames!)
        {
            foreach (var world in frame.Worlds)
            {
                foreach (var scene in world.Scenes)
                {
                    var sceneKey = $"{world.Name}/{scene.Name}";
                    foreach (var store in scene.ComponentStores)
                    {
                        AnalyzeStoreBlock(
                            store,
                            sceneKey,
                            serializer,
                            structuredOptions,
                            types,
                            fields,
                            components,
                            entities,
                            scenes,
                            total);
                    }
                }
            }
        }

        return new GameDebugDumpAnalysisReport(
            dump.Format,
            dump.Version,
            dump.Name,
            dump.Frames.Count,
            serializedBytes,
            total.ToBreakdown(),
            Sort(types),
            Sort(fields),
            Sort(components),
            Sort(entities),
            Sort(scenes));
    }

    private static void AnalyzeStoreBlock(
        GameDebugDumpComponentStoreBlock store,
        string sceneKey,
        OdinGameDebugComponentValueSerializer serializer,
        GameDebugStructuredComponentValueCaptureOptions structuredOptions,
        Dictionary<string, Accumulator> types,
        Dictionary<string, Accumulator> fields,
        Dictionary<string, Accumulator> components,
        Dictionary<string, Accumulator> entities,
        Dictionary<string, Accumulator> scenes,
        Accumulator total)
    {
        var typeKey = CreateTypeKey(store.Type);
        if (StringComparer.Ordinal.Equals(store.Format, GameDebugComponentStoreBlockSerializer.StructuredFormat) &&
            GameDebugComponentStoreBlockSerializer.TryDeserializeStructuredValues(store, out var structuredValues, out _))
        {
            AnalyzeStructuredStoreBlock(
                store,
                structuredValues,
                sceneKey,
                typeKey,
                types,
                fields,
                components,
                entities,
                scenes,
                total);
            return;
        }

        Array values;
        try
        {
            values = GameDebugComponentStoreBlockSerializer.DeserializeValues(store);
        }
        catch
        {
            Add(types, typeKey, store.Type.Name, store.Payload.LongLength, store.Payload.LongLength, 0);
            Add(scenes, sceneKey, sceneKey, store.Payload.LongLength, store.Payload.LongLength, 0);
            total.Add(store.Payload.LongLength, store.Payload.LongLength, 0);
            return;
        }

        if (values.Length == 0)
        {
            Add(types, typeKey, store.Type.Name, store.Payload.LongLength, store.Payload.LongLength, 0);
            Add(scenes, sceneKey, sceneKey, store.Payload.LongLength, store.Payload.LongLength, 0);
            total.Add(store.Payload.LongLength, store.Payload.LongLength, 0);
            return;
        }

        for (var row = 0; row < values.Length; row++)
        {
            var entityId = store.EntityIds[row];
            var entityKey = $"{sceneKey}/entity/{entityId}";
            var componentKey = $"{entityKey}/component/{typeKey}";
            var payloadBytes = DistributeBytes(store.Payload.LongLength, row, values.Length);
            var value = values.GetValue(row);
            var structured = value is null
                ? null
                : serializer.Serialize(
                    value,
                    new GameDebugComponentValueSerializationOptions
                    {
                        IncludePayload = false,
                        IncludeStructured = true,
                        StructuredCaptureOptions = structuredOptions,
                    }).Structured;
            var structuredBytes = structured is null ? 0 : EstimateStructuredBytes(structured);
            var totalBytes = payloadBytes + structuredBytes;

            Add(types, typeKey, store.Type.Name, totalBytes, payloadBytes, structuredBytes);
            Add(components, componentKey, store.Type.Name, totalBytes, payloadBytes, structuredBytes);
            Add(entities, entityKey, $"Entity {entityId}", totalBytes, payloadBytes, structuredBytes);
            Add(scenes, sceneKey, sceneKey, totalBytes, payloadBytes, structuredBytes);
            total.Add(totalBytes, payloadBytes, structuredBytes);

            if (structured is null)
            {
                continue;
            }

            if (structured.Children.Count == 0)
            {
                AddFields(fields, typeKey, store.Type.Name, structured, string.Empty);
            }
            else
            {
                foreach (var child in structured.Children)
                {
                    AddFields(fields, typeKey, store.Type.Name, child, string.Empty);
                }
            }
        }
    }

    private static void AnalyzeStructuredStoreBlock(
        GameDebugDumpComponentStoreBlock store,
        IReadOnlyList<ComponentValueDebugSnapshot> values,
        string sceneKey,
        string typeKey,
        Dictionary<string, Accumulator> types,
        Dictionary<string, Accumulator> fields,
        Dictionary<string, Accumulator> components,
        Dictionary<string, Accumulator> entities,
        Dictionary<string, Accumulator> scenes,
        Accumulator total)
    {
        if (values.Count == 0)
        {
            Add(types, typeKey, store.Type.Name, store.Payload.LongLength, 0, 0);
            Add(scenes, sceneKey, sceneKey, store.Payload.LongLength, 0, 0);
            total.Add(store.Payload.LongLength, 0, 0);
            return;
        }

        for (var row = 0; row < values.Count; row++)
        {
            var entityId = store.EntityIds[row];
            var entityKey = $"{sceneKey}/entity/{entityId}";
            var componentKey = $"{entityKey}/component/{typeKey}";
            var structured = values[row].Structured;
            var structuredBytes = structured is null ? 0 : EstimateStructuredBytes(structured);
            var payloadBytes = DistributeBytes(store.Payload.LongLength, row, values.Count);
            var totalBytes = payloadBytes + structuredBytes;

            Add(types, typeKey, store.Type.Name, totalBytes, payloadBytes, structuredBytes);
            Add(components, componentKey, store.Type.Name, totalBytes, payloadBytes, structuredBytes);
            Add(entities, entityKey, $"Entity {entityId}", totalBytes, payloadBytes, structuredBytes);
            Add(scenes, sceneKey, sceneKey, totalBytes, payloadBytes, structuredBytes);
            total.Add(totalBytes, payloadBytes, structuredBytes);

            if (structured is null)
            {
                continue;
            }

            if (structured.Children.Count == 0)
            {
                AddFields(fields, typeKey, store.Type.Name, structured, string.Empty);
            }
            else
            {
                foreach (var child in structured.Children)
                {
                    AddFields(fields, typeKey, store.Type.Name, child, string.Empty);
                }
            }
        }
    }

    private static void AddFields(
        Dictionary<string, Accumulator> fields,
        string typeKey,
        string typeName,
        ComponentValueDebugNode node,
        string path)
    {
        var currentPath = string.IsNullOrWhiteSpace(node.Name)
            ? path
            : string.IsNullOrEmpty(path)
                ? node.Name
                : path + "." + node.Name;

        if (!string.IsNullOrEmpty(currentPath))
        {
            var bytes = EstimateStructuredBytes(node);
            Add(fields, $"{typeKey}.{currentPath}", $"{typeName}.{currentPath}", bytes, 0, bytes);
        }

        foreach (var child in node.Children)
        {
            AddFields(fields, typeKey, typeName, child, currentPath);
        }
    }

    private static void Add(
        Dictionary<string, Accumulator> values,
        string key,
        string? displayName,
        long totalBytes,
        long payloadBytes,
        long structuredBytes)
    {
        if (!values.TryGetValue(key, out var accumulator))
        {
            accumulator = new Accumulator(key, displayName);
            values.Add(key, accumulator);
        }

        accumulator.Add(totalBytes, payloadBytes, structuredBytes);
    }

    private static IReadOnlyList<GameDebugDumpAnalysisEntry> Sort(Dictionary<string, Accumulator> values)
    {
        return values.Values
            .OrderByDescending(static value => value.TotalBytes)
            .ThenBy(static value => value.Key, StringComparer.Ordinal)
            .Select(static value => value.ToEntry())
            .ToArray();
    }

    private static int EstimateComponentBytes(ComponentDebugSnapshot component)
    {
        var total = 64;
        total += EstimateTypeBytes(component.Type);
        total += EstimateComponentValueBytes(component.Value);
        total += EstimateGraphBytes(component.Graph);
        return total;
    }

    private static int EstimateComponentValueBytes(ComponentValueDebugSnapshot value)
    {
        var total = 32;
        total += GetStringBytes(value.Format);
        total += GetStringBytes(value.Payload);
        total += GetStringBytes(value.Error);
        if (value.Structured is not null)
        {
            total += EstimateStructuredBytes(value.Structured);
        }

        return total;
    }

    private static int EstimateGraphBytes(ComponentGraphDebugSnapshot graph)
    {
        var total = 32;
        total += GetStringBytes(graph.Id);
        total += GetStringBytes(graph.ParentId);
        total += GetStringBytes(graph.Group);
        if (graph.ParentType is not null)
        {
            total += EstimateTypeBytes(graph.ParentType);
        }

        return total;
    }

    private static int EstimateTypeBytes(DebugTypeInfo type)
    {
        return 24 +
            GetStringBytes(type.Name) +
            GetStringBytes(type.FullName) +
            GetStringBytes(type.AssemblyName);
    }

    private static int EstimateStructuredBytes(ComponentValueDebugNode node)
    {
        var total = 32;
        total += GetStringBytes(node.Kind);
        total += GetStringBytes(node.Name);
        total += EstimateTypeBytes(node.Type);
        total += GetStringBytes(node.Value);
        total += GetStringBytes(node.Error);
        if (node.ElementType is not null)
        {
            total += EstimateTypeBytes(node.ElementType);
        }

        foreach (var option in node.Options)
        {
            total += GetStringBytes(option);
        }

        foreach (var child in node.Children)
        {
            total += EstimateStructuredBytes(child);
        }

        if (node.ElementTemplate is not null)
        {
            total += EstimateStructuredBytes(node.ElementTemplate);
        }

        return total;
    }

    private static int GetStringBytes(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? 0
            : Encoding.UTF8.GetByteCount(value);
    }

    private static long DistributeBytes(long bytes, int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var quotient = bytes / count;
        var remainder = bytes % count;
        return quotient + (index < remainder ? 1 : 0);
    }

    private static string CreateTypeKey(DebugTypeInfo type)
    {
        return $"{type.AssemblyName}:{type.FullName}";
    }

    private static void AppendSection(
        StringBuilder builder,
        string title,
        IReadOnlyList<GameDebugDumpAnalysisEntry> entries,
        int limit)
    {
        builder.AppendLine();
        builder.AppendLine(title);
        builder.AppendLine("Key | Count | Total | Payload | Structured");
        foreach (var entry in entries.Take(limit))
        {
            builder
                .Append(entry.DisplayName ?? entry.Key)
                .Append(" | ")
                .Append(entry.Count)
                .Append(" | ")
                .Append(entry.Size.TotalBytes)
                .Append(" | ")
                .Append(entry.Size.PayloadBytes)
                .Append(" | ")
                .Append(entry.Size.StructuredBytes)
                .AppendLine();
        }
    }

    private sealed class Accumulator(string key, string? displayName)
    {
        public string Key { get; } = key;

        public string? DisplayName { get; } = displayName;

        public long TotalBytes { get; private set; }

        public long PayloadBytes { get; private set; }

        public long StructuredBytes { get; private set; }

        public int Count { get; private set; }

        public void Add(long totalBytes, long payloadBytes, long structuredBytes)
        {
            TotalBytes += totalBytes;
            PayloadBytes += payloadBytes;
            StructuredBytes += structuredBytes;
            Count++;
        }

        public GameDebugDumpAnalysisEntry ToEntry()
        {
            return new GameDebugDumpAnalysisEntry(
                Key,
                DisplayName,
                ToBreakdown(),
                Count);
        }

        public GameDebugDumpAnalysisSizeBreakdown ToBreakdown()
        {
            return new GameDebugDumpAnalysisSizeBreakdown(
                TotalBytes,
                PayloadBytes,
                StructuredBytes);
        }
    }
}
