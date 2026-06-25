namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugOptions
{
    public bool EnableMutations { get; set; }

    public string DumpDirectory { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "NKGGameFramework",
        "debug-dumps");

    public bool DumpIncludeComponentPayloads { get; set; } = true;

    public bool DumpIncludeStructuredComponentValues { get; set; } = true;
}
