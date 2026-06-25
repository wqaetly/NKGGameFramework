using NKGGameFramework.Serialization;

namespace NKGGameFramework.Hosting.Diagnostics;

public static class GameDebugDumpFile
{
    public const string FileExtension = ".nkgdump";

    private static readonly OdinGameSerializer Serializer = new();

    public static byte[] Serialize(GameDebugDumpDocument dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        return Serializer.SerializeToBytes(dump);
    }

    public static GameDebugDumpDocument Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
        {
            throw new InvalidDataException("The debug dump file was empty.");
        }

        return Serializer.DeserializeFromBytes<GameDebugDumpDocument>(payload)
            ?? throw new InvalidDataException("The debug dump file could not be deserialized.");
    }
}
