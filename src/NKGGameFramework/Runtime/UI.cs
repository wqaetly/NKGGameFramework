using Cysharp.Threading.Tasks;

namespace NKGGameFramework.Runtime;

public interface IViewHandle : IDisposable
{
    string Location { get; }
}

public interface IUIService
{
    UniTask<IViewHandle> OpenAsync(string location, CancellationToken cancellationToken = default);
}
