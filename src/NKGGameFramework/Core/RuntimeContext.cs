using NKGGameFramework.Diagnostics;

namespace NKGGameFramework.Core;

public interface IRuntimeContext : IGameLoop, IDisposable
{
    IEventBus Events { get; }

    GameFrameTime Time { get; }

    T RegisterModule<T>(T module)
        where T : Module;

    T GetModule<T>()
        where T : class;

    bool TryGetModule<T>(out T? module)
        where T : class;

    void Shutdown();
}

public sealed class RuntimeContext : IRuntimeContext
{
    private readonly Dictionary<Type, Module> _modulesByConcreteType = [];
    private readonly List<Module> _modules = [];
    private bool _disposed;
    private bool _updateListDirty;
    private List<IUpdateModule> _updateModules = [];

    public RuntimeContext(IEventBus? events = null)
    {
        Events = events ?? new EventBus();
        GameDebugRuntimeRegistry.Register(this);
    }

    public IEventBus Events { get; }

    public GameFrameTime Time { get; private set; } = GameFrameTime.Zero;

    public IReadOnlyList<Module> Modules => _modules;

    public bool IsDisposed => _disposed;

    public T RegisterModule<T>(T module)
        where T : Module
    {
        ArgumentNullException.ThrowIfNull(module);
        ThrowIfDisposed();

        var type = module.GetType();
        if (_modulesByConcreteType.ContainsKey(type))
        {
            throw new InvalidOperationException($"Module '{type.Name}' is already registered.");
        }

        _modulesByConcreteType.Add(type, module);
        _modules.Add(module);
        _updateListDirty = true;

        module.Initialize(this);
        return module;
    }

    public T GetModule<T>()
        where T : class
    {
        return TryGetModule<T>(out var module)
            ? module!
            : throw new KeyNotFoundException($"Module '{typeof(T).Name}' is not registered.");
    }

    public bool TryGetModule<T>(out T? module)
        where T : class
    {
        ThrowIfDisposed();

        if (_modulesByConcreteType.TryGetValue(typeof(T), out var exactModule))
        {
            module = (T)(object)exactModule;
            return true;
        }

        var matches = _modules.OfType<T>().Take(2).ToArray();
        if (matches.Length == 1)
        {
            module = matches[0];
            return true;
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException($"More than one module can be assigned to '{typeof(T).Name}'. Use a concrete module type.");
        }

        module = null;
        return false;
    }

    public void Update(in GameFrameTime time)
    {
        ThrowIfDisposed();
        if (!GameDebugController.Shared.TryBeginRuntimeFrame())
        {
            return;
        }

        RebuildUpdateListIfNeeded();
        Time = time;

        foreach (var module in _updateModules)
        {
            module.Update(in time);
        }

        Events.DispatchQueuedEvents();
        GameDebugFramePublisher.Shared.Publish(nameof(RuntimeContext), Time.Frame);
    }

    public void Update(double deltaTime, double realDeltaTime)
    {
        var time = GameFrameTime.Advance(Time, deltaTime, realDeltaTime);
        Update(in time);
    }

    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var module in _modules.OrderBy(static module => module.Priority).ToArray())
        {
            module.Shutdown();
        }

        _modules.Clear();
        _modulesByConcreteType.Clear();
        _updateModules.Clear();
        Events.Clear();
        _disposed = true;
        GameDebugRuntimeRegistry.Unregister(this);
    }

    public void Dispose()
    {
        Shutdown();
    }

    private void RebuildUpdateListIfNeeded()
    {
        if (!_updateListDirty)
        {
            return;
        }

        _updateModules = [.. _modules
            .OfType<IUpdateModule>()
            .OrderByDescending(static module => module is Module m ? m.Priority : 0)];
        _updateListDirty = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
