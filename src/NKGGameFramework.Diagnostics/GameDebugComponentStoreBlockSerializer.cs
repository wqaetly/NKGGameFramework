using NKGGameFramework.Ecs;
using OdinSerializer;

namespace NKGGameFramework.Diagnostics;

internal static class GameDebugComponentStoreBlockSerializer
{
    public const string Format = "odin-binary-array";

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
            return new GameDebugDumpComponentStoreBlock(
                DebugSnapshotTypeNames.Create(block.ComponentType),
                block.EntityIds,
                Format,
                [],
                exception.Message);
        }
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
