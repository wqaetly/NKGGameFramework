using System.Text;

namespace NKGGameFramework.Diagnostics;

public static class GameDebugDumpFile
{
    public const string FileExtension = ".nkgdump";
    public const string ContainerMagicText = "NKGDUMP4\n";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes(ContainerMagicText);

    public static byte[] Serialize(GameDebugDumpDocument dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        var payload = GameDebugDumpBinaryCodec.Serialize(dump);
        using var output = new MemoryStream();
        output.Write(Magic);
        output.Write(payload);

        return output.ToArray();
    }

    public static GameDebugDumpDocument Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
        {
            throw new InvalidDataException("The debug dump file was empty.");
        }

        if (HasMagic(payload))
        {
            var documentPayload = new byte[payload.Length - Magic.Length];
            Array.Copy(payload, Magic.Length, documentPayload, 0, documentPayload.Length);
            return GameDebugDumpBinaryCodec.Deserialize(documentPayload);
        }

        throw new InvalidDataException("The debug dump file was not a supported NKG dump.");
    }

    private static bool HasMagic(byte[] payload)
    {
        return HasMagic(payload, Magic);
    }

    private static bool HasMagic(byte[] payload, byte[] magic)
    {
        if (payload.Length < magic.Length)
        {
            return false;
        }

        for (var index = 0; index < magic.Length; index++)
        {
            if (payload[index] != magic[index])
            {
                return false;
            }
        }

        return true;
    }
}
