using System.Text.Json;

namespace NKGGameFramework.Hosting.Diagnostics;

internal static class GameDebugJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
}
