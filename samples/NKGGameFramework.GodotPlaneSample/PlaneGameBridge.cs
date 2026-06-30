using System.Globalization;
using NKGGameFramework.Adapter.Godot;
using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.GodotPlaneSample;

public static class PlaneGameBridge
{
    private const double StepSeconds = 1.0d / PlaneGameRules.SimulationHz;
    private static PlaneGame? s_session;
    private static GodotDebugEndpointBridge? s_debug;
    private static int s_moveX;
    private static int s_moveY;
    private static bool s_fire;
    private static string s_hostStatus = "boot";
    private static int s_debugPort;

    public static void ResetSession()
    {
        s_debug?.Dispose();
        s_session?.Dispose();
        s_session = new PlaneGame();
        s_debug = CreateDebugBridge(s_session);
        s_session.Start();
        ApplyHostContext(s_session);
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

    public static string UpdateHostContext(string context)
    {
        var separator = context.IndexOf('\n', StringComparison.Ordinal);
        if (separator < 0)
        {
            s_hostStatus = context;
            s_debugPort = 0;
        }
        else
        {
            s_hostStatus = context[..separator];
            s_debugPort = int.TryParse(context[(separator + 1)..], CultureInfo.InvariantCulture, out var port)
                ? port
                : 0;
        }

        if (s_session is not null)
        {
            ApplyHostContext(s_session);
        }

        return "ok";
    }

    public static string StepSession()
    {
        s_session ??= CreateStartedSession();
        s_debug ??= CreateDebugBridge(s_session);
        ApplyHostContext(s_session);
        if (s_session.IsGameOver)
        {
            return s_session.CreateSnapshot();
        }

        s_session.SetInput(ClampAxis(s_moveX), ClampAxis(s_moveY), s_fire);
        s_session.Update(StepSeconds);
        return s_session.CreateSnapshot();
    }

    public static byte[] StepSessionCommandBytes()
    {
        s_session ??= CreateStartedSession();
        s_debug ??= CreateDebugBridge(s_session);
        ApplyHostContext(s_session);
        if (!s_session.IsGameOver)
        {
            s_session.SetInput(ClampAxis(s_moveX), ClampAxis(s_moveY), s_fire);
            s_session.Update(StepSeconds);
        }

        return s_session.CreateCommandBytes();
    }

    public static string GetSessionStatus()
    {
        if (s_session is null)
        {
            return "idle";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"score={s_session.Score} lives={s_session.Lives} game_over={s_session.IsGameOver} player_x={s_session.PlayerX:0.###} bullets={s_session.BulletCount} objects={s_session.VisualObjectCount}");
    }

    public static string HandleDebugRequest(string request)
    {
        s_session ??= CreateStartedSession();
        s_debug ??= CreateDebugBridge(s_session);
        return s_debug.Handle(request);
    }

    private static PlaneGame CreateStartedSession()
    {
        var game = new PlaneGame();
        s_debug = CreateDebugBridge(game);
        game.Start();
        return game;
    }

    private static void ApplyHostContext(PlaneGame game)
    {
        game.HostStatus = s_hostStatus;
        game.DebugPort = s_debugPort;
    }

    private static GodotDebugEndpointBridge CreateDebugBridge(PlaneGame game)
    {
        var session = new GameDebugSession()
            .Register(game.Runtime)
            .Register(game.World);
        return new GodotDebugEndpointBridge(new GodotDebugEndpointBridgeOptions
        {
            Session = session,
            DumpRecordingFrameStride = 4,
        });
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
