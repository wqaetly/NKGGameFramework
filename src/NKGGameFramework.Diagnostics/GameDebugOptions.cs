namespace NKGGameFramework.Diagnostics;

public sealed class GameDebugOptions
{
    public bool EnableMutations { get; set; }

    public string DumpDirectory { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "NKGGameFramework",
        "debug-dumps");
}
