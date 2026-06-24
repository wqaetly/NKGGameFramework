using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Diagnostics;

public static class GameDebugRuntimeRegistry
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<RuntimeContext>> RuntimeContexts = [];
    private static readonly List<WeakReference<World>> Worlds = [];

    public static void Register(RuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);

        lock (Gate)
        {
            Add(RuntimeContexts, runtimeContext);
        }
    }

    public static void Register(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        lock (Gate)
        {
            Add(Worlds, world);
        }
    }

    public static bool Unregister(RuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);

        lock (Gate)
        {
            return Remove(RuntimeContexts, runtimeContext);
        }
    }

    public static bool Unregister(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        lock (Gate)
        {
            return Remove(Worlds, world);
        }
    }

    public static IReadOnlyList<RuntimeContext> GetRuntimeContexts()
    {
        lock (Gate)
        {
            return Compact(RuntimeContexts);
        }
    }

    public static IReadOnlyList<World> GetWorlds()
    {
        lock (Gate)
        {
            return Compact(Worlds);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            RuntimeContexts.Clear();
            Worlds.Clear();
        }
    }

    private static void Add<T>(List<WeakReference<T>> references, T instance)
        where T : class
    {
        for (var index = references.Count - 1; index >= 0; index--)
        {
            if (!references[index].TryGetTarget(out var target))
            {
                references.RemoveAt(index);
                continue;
            }

            if (ReferenceEquals(target, instance))
            {
                return;
            }
        }

        references.Add(new WeakReference<T>(instance));
    }

    private static bool Remove<T>(List<WeakReference<T>> references, T instance)
        where T : class
    {
        var removed = false;
        for (var index = references.Count - 1; index >= 0; index--)
        {
            if (!references[index].TryGetTarget(out var target))
            {
                references.RemoveAt(index);
                continue;
            }

            if (!ReferenceEquals(target, instance))
            {
                continue;
            }

            references.RemoveAt(index);
            removed = true;
        }

        return removed;
    }

    private static IReadOnlyList<T> Compact<T>(List<WeakReference<T>> references)
        where T : class
    {
        var live = new List<T>(references.Count);
        for (var index = references.Count - 1; index >= 0; index--)
        {
            if (!references[index].TryGetTarget(out var target))
            {
                references.RemoveAt(index);
                continue;
            }

            live.Add(target);
        }

        live.Reverse();
        return live;
    }
}
