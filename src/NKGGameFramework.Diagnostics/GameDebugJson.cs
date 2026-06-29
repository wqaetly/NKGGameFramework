using System.Text.Json;

namespace NKGGameFramework.Diagnostics;

public static class GameDebugJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
}
