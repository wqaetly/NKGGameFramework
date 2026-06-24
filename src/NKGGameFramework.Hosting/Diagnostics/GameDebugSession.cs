using NKGGameFramework.Core;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class GameDebugSession
{
    private readonly object _gate = new();
    private readonly List<RuntimeContext> _runtimeContexts = [];
    private readonly List<World> _worlds = [];

    public GameDebugSession Register(RuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);

        lock (_gate)
        {
            if (!_runtimeContexts.Contains(runtimeContext))
            {
                _runtimeContexts.Add(runtimeContext);
            }
        }

        return this;
    }

    public GameDebugSession Register(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        lock (_gate)
        {
            if (!_worlds.Contains(world))
            {
                _worlds.Add(world);
            }
        }

        return this;
    }

    public bool Unregister(RuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);

        lock (_gate)
        {
            return _runtimeContexts.Remove(runtimeContext);
        }
    }

    public bool Unregister(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        lock (_gate)
        {
            return _worlds.Remove(world);
        }
    }

    public IReadOnlyList<RuntimeContext> GetRuntimeContexts()
    {
        lock (_gate)
        {
            return _runtimeContexts.Count > 0
                ? _runtimeContexts.ToArray()
                : GameDebugRuntimeRegistry.GetRuntimeContexts();
        }
    }

    public IReadOnlyList<World> GetWorlds()
    {
        lock (_gate)
        {
            return _worlds.Count > 0
                ? _worlds.ToArray()
                : GameDebugRuntimeRegistry.GetWorlds();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _runtimeContexts.Clear();
            _worlds.Clear();
        }
    }
}
