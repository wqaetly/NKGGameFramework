using Cysharp.Threading.Tasks;

namespace NKGGameFramework.Runtime;

public interface IAssetHandle<TAsset> : IDisposable
    where TAsset : class
{
    string Location { get; }

    TAsset Asset { get; }

    int ReferenceCount { get; }

    IAssetHandle<TAsset> Retain();

    void Release();
}

public interface IAssetService
{
    UniTask<IAssetHandle<TAsset>> LoadAsync<TAsset>(string location, CancellationToken cancellationToken = default)
        where TAsset : class;
}

public sealed class AssetHandle<TAsset> : IAssetHandle<TAsset>
    where TAsset : class
{
    private readonly Action<AssetHandle<TAsset>>? _release;
    private int _referenceCount = 1;

    public AssetHandle(string location, TAsset asset, Action<AssetHandle<TAsset>>? release = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(asset);

        Location = location;
        Asset = asset;
        _release = release;
    }

    public string Location { get; }

    public TAsset Asset { get; }

    public int ReferenceCount => _referenceCount;

    public IAssetHandle<TAsset> Retain()
    {
        if (_referenceCount <= 0)
        {
            throw new ObjectDisposedException(nameof(AssetHandle<TAsset>));
        }

        _referenceCount++;
        return this;
    }

    public void Release()
    {
        if (_referenceCount <= 0)
        {
            throw new ObjectDisposedException(nameof(AssetHandle<TAsset>));
        }

        _referenceCount--;
        if (_referenceCount == 0)
        {
            _release?.Invoke(this);
        }
    }

    public void Dispose()
    {
        while (_referenceCount > 0)
        {
            Release();
        }
    }
}
