using NKGGameFramework.Hosting.Diagnostics;

namespace NKGGameFramework.SkillSystemSampler;

internal static class Program
{
    private const string HoldSecondsVariable = "NKG_DEBUG_SAMPLE_HOLD_SECONDS";

    public static async Task Main()
    {
        var debugHost = await GameDebugHostAutoStart.TryStartFromEnvironmentAsync();
        if (debugHost is not null)
        {
            SampleLog.Write($"Web Debug Host={debugHost.BaseAddress}");
        }

        using var world = SkillSystemSample.Run();

        if (debugHost is not null)
        {
            var holdSeconds = ReadHoldSeconds();
            if (holdSeconds > 0)
            {
                SampleLog.Write($"Web Debug Host hold {holdSeconds:0.##}s for external debug requests.");
                await Task.Delay(TimeSpan.FromSeconds(holdSeconds));
            }
        }
    }

    private static double ReadHoldSeconds()
    {
        var value = Environment.GetEnvironmentVariable(HoldSecondsVariable);
        return double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var seconds)
            ? seconds
            : 10;
    }
}
