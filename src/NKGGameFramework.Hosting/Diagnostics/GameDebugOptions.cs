namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugOptions
{
    public bool EnableMutations { get; set; } = true;

    public string DumpDirectory { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "NKGGameFramework",
        "debug-dumps");
}
