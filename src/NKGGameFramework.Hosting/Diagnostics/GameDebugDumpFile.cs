using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace NKGGameFramework.Hosting.Diagnostics;

public static class GameDebugDumpFile
{
    public const string FileExtension = ".nkgdump";
    public const string ContainerMagicText = "NKGDUMP3\n";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes(ContainerMagicText);

    public static byte[] Serialize(GameDebugDumpDocument dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        var json = JsonSerializer.SerializeToUtf8Bytes(dump, GameDebugJson.Options);
        using var output = new MemoryStream();
        output.Write(Magic);
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(json);
        }

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
            using var compressed = new MemoryStream(payload, Magic.Length, payload.Length - Magic.Length);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<GameDebugDumpDocument>(gzip, GameDebugJson.Options)
                ?? throw new InvalidDataException("The debug dump file could not be deserialized.");
        }

        throw new InvalidDataException("The debug dump file was not a supported NKG dump.");
    }

    private static bool HasMagic(byte[] payload)
    {
        if (payload.Length < Magic.Length)
        {
            return false;
        }

        for (var index = 0; index < Magic.Length; index++)
        {
            if (payload[index] != Magic[index])
            {
                return false;
            }
        }

        return true;
    }
}
