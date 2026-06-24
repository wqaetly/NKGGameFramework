using NKGGameFramework.Core;

namespace NKGGameFramework.Sampler;

internal static class Program
{
    public static void Main()
    {
        // 示例程序用普通控制台主循环模拟宿主。
        // 接入具体宿主时，只需要把启动、更新、关闭映射到各自生命周期。
        using var game = new SampleGame();

        game.Start();

        // 运行时上下文的更新方法是框架的统一驱动入口：
        // 流程、模块更新、队列事件派发都会从这里推进。
        var time = GameFrameTime.Zero;
        while (game.IsRunning)
        {
            time = GameFrameTime.Advance(time, deltaTime: 1.0, realDeltaTime: 1.0);
            game.Update(in time);
        }

        game.Shutdown();
    }
}
