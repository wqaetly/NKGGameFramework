using NKGGameFramework.Ecs;
using OdinSerializer;

namespace NKGGameFramework.Diagnostics;

internal static class GameDebugComponentStoreBlockSerializer
{
    public const string Format = "odin-binary-array";
    public const string StructuredFormat = "structured-component-values-v1";

    public static GameDebugDumpComponentStoreBlock Serialize(EcsComponentStoreDumpBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        try
        {
            return new GameDebugDumpComponentStoreBlock(
                DebugSnapshotTypeNames.Create(block.ComponentType),
                block.EntityIds,
                Format,
                SerializationUtility.SerializeValueWeak(
                    block.Values,
                    DataFormat.Binary,
                    GameDebugOdinSerialization.CreateSerializationContext()),
                Error: null);
        }
        catch (Exception exception)
        {
            try
            {
                return SerializeStructuredFallback(block);
            }
            catch
            {
            }

            return new GameDebugDumpComponentStoreBlock(
                DebugSnapshotTypeNames.Create(block.ComponentType),
                block.EntityIds,
                Format,
                [],
                exception.Message);
        }
    }

    public static bool TryDeserializeStructuredValues(
        GameDebugDumpComponentStoreBlock block,
        out ComponentValueDebugSnapshot[] values,
        out string? error)
    {
        values = [];
        error = null;
        if (!StringComparer.Ordinal.Equals(block.Format, StructuredFormat))
        {
            error = $"Unsupported component store block format '{block.Format}'.";
            return false;
        }

        try
        {
            values = GameDebugDumpBinaryCodec.DeserializeComponentValues(block.Payload);
            if (values.Length != block.EntityIds.Length)
            {
                error = "The structured component block entity index did not match the value length.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static bool TryGetStructuredValue(
        GameDebugDumpComponentStoreBlock block,
        int entityId,
        out ComponentValueDebugSnapshot value,
        out string? error)
    {
        value = null!;
        var row = Array.IndexOf(block.EntityIds, entityId);
        if (row < 0)
        {
            error = $"Entity {entityId} was not present in component store '{block.Type.Name}'.";
            return false;
        }

        if (!TryDeserializeStructuredValues(block, out var values, out error))
        {
            return false;
        }

        value = values[row];
        return true;
    }

    public static Array DeserializeValues(GameDebugDumpComponentStoreBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (!StringComparer.Ordinal.Equals(block.Format, Format))
        {
            throw new InvalidDataException($"Unsupported component store block format '{block.Format}'.");
        }

        if (!string.IsNullOrWhiteSpace(block.Error))
        {
            throw new InvalidDataException(block.Error);
        }

        if (block.Payload.Length == 0)
        {
            if (block.EntityIds.Length != 0)
            {
                throw new InvalidDataException("The component store block entity index did not match the payload length.");
            }

            return Array.Empty<object>();
        }

        var values = SerializationUtility.DeserializeValueWeak(
            block.Payload,
            DataFormat.Binary,
            GameDebugOdinSerialization.CreateDeserializationContext());

        if (values is not Array array)
        {
            throw new InvalidDataException("The component store block did not contain an array payload.");
        }

        if (array.Length != block.EntityIds.Length)
        {
            throw new InvalidDataException("The component store block entity index did not match the payload length.");
        }

        return array;
    }

    private static GameDebugDumpComponentStoreBlock SerializeStructuredFallback(EcsComponentStoreDumpBlock block)
    {
        var serializer = new OdinGameDebugComponentValueSerializer();
        var values = new ComponentValueDebugSnapshot[block.Values.Length];
        for (var index = 0; index < block.Values.Length; index++)
        {
            var value = block.Values.GetValue(index);
            values[index] = serializer.Serialize(
                value!,
                new GameDebugComponentValueSerializationOptions
                {
                    IncludePayload = false,
                    IncludeStructured = true,
                    StructuredCaptureOptions = new GameDebugStructuredComponentValueCaptureOptions
                    {
                        MaxCollectionItems = 64,
                        CaptureElementTemplate = false,
                    },
                });
        }

        return new GameDebugDumpComponentStoreBlock(
            DebugSnapshotTypeNames.Create(block.ComponentType),
            block.EntityIds,
            StructuredFormat,
            GameDebugDumpBinaryCodec.SerializeComponentValues(values),
            Error: null);
    }

    public static bool TryGetValue(
        GameDebugDumpComponentStoreBlock block,
        int entityId,
        out object value,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(block);

        var row = Array.IndexOf(block.EntityIds, entityId);
        if (row < 0)
        {
            value = null!;
            error = $"Entity {entityId} was not present in component store '{block.Type.Name}'.";
            return false;
        }

        try
        {
            var values = DeserializeValues(block);
            value = values.GetValue(row)
                ?? throw new InvalidDataException("The component store block contained a null row.");
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            value = null!;
            error = exception.Message;
            return false;
        }
    }

}
