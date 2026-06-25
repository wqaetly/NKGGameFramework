using NKGGameFramework.Core;
using NKGGameFramework.Hosting.Diagnostics;

namespace NKGGameFramework.SkillSystemSampler;

internal static class Program
{
    private static readonly TimeSpan FrameDelay = TimeSpan.FromMilliseconds(100);
    private static readonly GameDebugHostStartupOptions DebugHostStartup =
        GameDebugHostStartupOptions.Localhost(port: 5068, enableMutations: true);

    public static async Task Main()
    {
        var debugHost = await GameDebugHostAutoStart.TryStartAsync(DebugHostStartup);
        if (debugHost is not null)
        {
            SampleLog.Write($"Web Debug Host={debugHost.BaseAddress}");
        }

        try
        {
            using var sample = SkillSystemSample.Start();
            SampleLog.Write("按任意键结束示例。");

            var exitKey = WaitForExitKeyAsync();
            var time = GameFrameTime.Zero;
            while (!exitKey.IsCompleted)
            {
                time = GameFrameTime.Advance(time, FrameDelay, FrameDelay);
                sample.Update(in time);
                await Task.Delay(FrameDelay);
            }

            await exitKey;
        }
        finally
        {
            await GameDebugHostAutoStart.StopAsync();
        }
    }

    private static Task<ConsoleKeyInfo> WaitForExitKeyAsync()
    {
        return Task.Run(() => Console.ReadKey(intercept: true));
    }
}
