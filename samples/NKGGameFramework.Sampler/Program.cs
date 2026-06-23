namespace NKGGameFramework.Sampler;

internal static class Program
{
    public static void Main()
    {
        // Sampler 用普通 console 主循环模拟宿主引擎。
        // Unity/Godot/Server 接入时，只需要把 Start/Update/Shutdown 映射到各自生命周期。
        using var game = new SampleGame();

        game.Start();

        // RuntimeContext.Update 是框架的统一驱动入口：
        // Procedure、模块更新、队列事件派发都会从这里推进。
        while (game.IsRunning)
        {
            game.Update(deltaTime: 1.0, realDeltaTime: 1.0);
        }

        game.Shutdown();
    }
}
