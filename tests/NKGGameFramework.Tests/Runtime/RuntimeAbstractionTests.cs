using Cysharp.Threading.Tasks;
using NKGGameFramework.Async;
using NKGGameFramework.Runtime;

namespace NKGGameFramework.Runtime.Tests;

public sealed class RuntimeAbstractionTests
{
    [Fact]
    public void AssetHandleReferenceCountsAndReleases()
    {
        var releases = 0;
        var handle = new AssetHandle<string>("hero/avatar", "asset", _ => releases++);

        handle.Retain();
        handle.Release();
        handle.Release();

        Assert.Equal(0, handle.ReferenceCount);
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task RuntimeAsyncContractsUseUniTask()
    {
        var assets = new InMemoryAssetService();
        assets.Add("hero/avatar", "asset");

        using var handle = await assets.LoadAsync<string>("hero/avatar");

        Assert.Equal("hero/avatar", handle.Location);
        Assert.Equal("asset", handle.Asset);
    }

    [Fact]
    public async Task GameAsyncComposesUniTaskResults()
    {
        var results = await GameAsync.WhenAll(
            GameAsync.FromResult(1),
            GameAsync.FromResult(2),
            GameAsync.FromResult(3));

        await GameAsync.CompletedTask;

        Assert.Equal([1, 2, 3], results);
    }

    private sealed class InMemoryAssetService : IAssetService
    {
        private readonly Dictionary<string, object> _assets = [];

        public void Add<TAsset>(string location, TAsset asset)
            where TAsset : class
        {
            _assets.Add(location, asset);
        }

        public UniTask<IAssetHandle<TAsset>> LoadAsync<TAsset>(string location, CancellationToken cancellationToken = default)
            where TAsset : class
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_assets.TryGetValue(location, out var asset))
            {
                throw new KeyNotFoundException(location);
            }

            return GameAsync.FromResult<IAssetHandle<TAsset>>(new AssetHandle<TAsset>(location, (TAsset)asset));
        }
    }
}
