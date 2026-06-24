using NKGGameFramework.Core;
using NKGGameFramework.Hosting.Diagnostics;

namespace NKGGameFramework.Sampler;

internal static class Program
{
    private static readonly TimeSpan FrameDelay = TimeSpan.FromMilliseconds(250);

    public static async Task Main()
    {
        // 示例程序用普通控制台主循环模拟宿主。
        // 接入具体宿主时，只需要把启动、更新、关闭映射到各自生命周期。
        var debugHost = await GameDebugHostAutoStart.TryStartFromEnvironmentAsync();
        if (debugHost is not null)
        {
            Console.WriteLine($"[基础示例] Web Debug Host={debugHost.BaseAddress}");
        }

        try
        {
            using var game = new SampleGame();

            game.Start();
            Console.WriteLine("[基础示例] 按任意键结束示例。");

            // 运行时上下文的更新方法是框架的统一驱动入口：
            // 流程、模块更新、队列事件派发都会从这里推进。
            var exitKey = WaitForExitKeyAsync();
            var time = GameFrameTime.Zero;
            while (game.IsRunning && !exitKey.IsCompleted)
            {
                time = GameFrameTime.Advance(time, FrameDelay, FrameDelay);
                game.Update(in time);
                await Task.Delay(FrameDelay);
            }

            game.RequestExit();
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
