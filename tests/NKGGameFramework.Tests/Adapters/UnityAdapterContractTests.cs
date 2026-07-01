using Cysharp.Threading.Tasks;
using NKGGameFramework.Adapter.Unity;
using NKGGameFramework.Async;
using NKGGameFramework.Core;
using NKGGameFramework.Runtime;

namespace NKGGameFramework.Tests.Adapters;

public sealed class UnityAdapterContractTests
{
    [Fact]
    public void DefaultTickCreatesFrameTimeFromUnityDeltaValues()
    {
        IUnityGameLoopDriver driver = new RecordingUnityDriver();

        driver.Tick(0.016, 0.033);

        var time = Assert.Single(((RecordingUnityDriver)driver).Ticks);
        Assert.Equal(0, time.Frame);
        Assert.Equal(TimeSpan.FromSeconds(0.016), time.DeltaTime);
        Assert.Equal(TimeSpan.FromSeconds(0.033), time.RealDeltaTime);
    }

    [Fact]
    public async Task UnityAssetServiceKeepsRuntimeAssetContract()
    {
        var assets = new TestUnityAssetService();
        assets.Add("prefabs/player", new object());

        using var handle = await assets.LoadAsync<object>("prefabs/player");

        Assert.IsAssignableFrom<IAssetService>(assets);
        Assert.Equal("prefabs/player", handle.Location);
        Assert.Equal(1, handle.ReferenceCount);
    }

    [Fact]
    public async Task UnitySceneServiceKeepsRuntimeSceneContract()
    {
        var scenes = new TestUnitySceneService();

        using var handle = await scenes.LoadSceneAsync("scenes/battle", SceneLoadMode.Single);

        Assert.IsAssignableFrom<ISceneService>(scenes);
        Assert.Equal("scenes/battle", handle.Location);
        Assert.Equal(("scenes/battle", SceneLoadMode.Single), scenes.Loaded.Single());
    }

    private sealed class RecordingUnityDriver : IUnityGameLoopDriver
    {
        public List<GameFrameTime> Ticks { get; } = [];

        public void Tick(in GameFrameTime time)
        {
            Ticks.Add(time);
        }
    }

    private sealed class TestUnityAssetService : IUnityAssetService
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
            return GameAsync.FromResult<IAssetHandle<TAsset>>(new AssetHandle<TAsset>(location, (TAsset)_assets[location]));
        }
    }

    private sealed class TestUnitySceneService : IUnitySceneService
    {
        public List<(string Location, SceneLoadMode Mode)> Loaded { get; } = [];

        public UniTask<ISceneHandle> LoadSceneAsync(string location, SceneLoadMode mode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Loaded.Add((location, mode));
            return GameAsync.FromResult<ISceneHandle>(new SceneHandle(location));
        }
    }
}
