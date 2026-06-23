using Cysharp.Threading.Tasks;

namespace NKGGameFramework.Runtime;

public enum SceneLoadMode
{
    Single,
    Additive,
}

public interface ISceneHandle : IDisposable
{
    string Location { get; }
}

public interface ISceneService
{
    UniTask<ISceneHandle> LoadSceneAsync(string location, SceneLoadMode mode, CancellationToken cancellationToken = default);
}

public sealed class SceneHandle : ISceneHandle
{
    private readonly Action<SceneHandle>? _unload;
    private bool _disposed;

    public SceneHandle(string location, Action<SceneHandle>? unload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        Location = location;
        _unload = unload;
    }

    public string Location { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unload?.Invoke(this);
    }
}
