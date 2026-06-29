using System.Globalization;

namespace NKGGameFramework.GodotPlaneSample;

public static class PlaneGameBridge
{
    private const double StepSeconds = 1.0d / PlaneGameRules.SimulationHz;
    private static PlaneGame? s_session;
    private static int s_moveX;
    private static int s_moveY;
    private static bool s_fire;

    public static void ResetSession()
    {
        s_session?.Dispose();
        s_session = new PlaneGame();
        s_session.Start();
        ClearInput();
    }

    public static void ClearInput()
    {
        s_moveX = 0;
        s_moveY = 0;
        s_fire = false;
    }

    public static void PressLeft() => s_moveX--;

    public static void PressRight() => s_moveX++;

    public static void PressUp() => s_moveY--;

    public static void PressDown() => s_moveY++;

    public static void PressFire() => s_fire = true;

    public static string StepSession()
    {
        s_session ??= CreateStartedSession();
        if (s_session.IsGameOver)
        {
            return s_session.CreateSnapshot();
        }

        s_session.SetInput(ClampAxis(s_moveX), ClampAxis(s_moveY), s_fire);
        s_session.Update(StepSeconds);
        return s_session.CreateSnapshot();
    }

    public static string GetSessionStatus()
    {
        if (s_session is null)
        {
            return "idle";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"score={s_session.Score} lives={s_session.Lives} game_over={s_session.IsGameOver}");
    }

    private static PlaneGame CreateStartedSession()
    {
        var game = new PlaneGame();
        game.Start();
        return game;
    }

    private static int ClampAxis(int value)
    {
        if (value < -1)
        {
            return -1;
        }

        return value > 1 ? 1 : value;
    }
}
