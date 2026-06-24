namespace NKGGameFramework.Diagnostics;

public sealed record GameDebugControlRequest(
    string Command,
    int? StepCount = null);

public sealed record GameDebugControlState(
    bool IsPaused,
    int PendingStepCount,
    long Revision,
    string? LastCommand);

public sealed record GameDebugControlResult(
    bool Succeeded,
    string Message,
    GameDebugControlState State);

public sealed class GameDebugController
{
    public static GameDebugController Shared { get; } = new();

    private readonly object _gate = new();
    private bool _isPaused;
    private int _pendingStepCount;
    private long _revision;
    private string? _lastCommand;

    public GameDebugControlState GetState()
    {
        lock (_gate)
        {
            return CreateState();
        }
    }

    public GameDebugControlResult Execute(GameDebugControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = request.Command.Trim().ToLowerInvariant();
        lock (_gate)
        {
            switch (command)
            {
                case "play":
                    _isPaused = false;
                    _pendingStepCount = 0;
                    return Success(command, "Debug playback resumed.");

                case "pause":
                    _isPaused = true;
                    return Success(command, "Debug playback paused.");

                case "step":
                    _isPaused = true;
                    _pendingStepCount += Math.Max(1, request.StepCount ?? 1);
                    return Success(command, $"Queued {_pendingStepCount} debug frame step(s).");

                default:
                    return new GameDebugControlResult(
                        false,
                        $"Unknown debug control command '{request.Command}'.",
                        CreateState());
            }
        }
    }

    internal bool TryBeginRuntimeFrame()
    {
        lock (_gate)
        {
            if (!_isPaused)
            {
                return true;
            }

            if (_pendingStepCount <= 0)
            {
                return false;
            }

            _pendingStepCount--;
            _revision++;
            _lastCommand = "step-consumed";
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _isPaused = false;
            _pendingStepCount = 0;
            _revision = 0;
            _lastCommand = null;
        }
    }

    private GameDebugControlResult Success(string command, string message)
    {
        _revision++;
        _lastCommand = command;
        return new GameDebugControlResult(true, message, CreateState());
    }

    private GameDebugControlState CreateState()
    {
        return new GameDebugControlState(
            _isPaused,
            _pendingStepCount,
            _revision,
            _lastCommand);
    }
}
