namespace NKGGameFramework.Core;

public interface IGameLoop
{
    void Update(in GameFrameTime time);

    void Update(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.FromSeconds(deltaTime, realDeltaTime);
        Update(in time);
    }
}
