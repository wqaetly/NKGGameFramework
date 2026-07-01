using Cysharp.Threading.Tasks;
using NKGGameFramework.Adapter.Godot;
using NKGGameFramework.Async;
using NKGGameFramework.Core;
using NKGGameFramework.Runtime;

namespace NKGGameFramework.Tests.Adapters;

public sealed class GodotAdapterContractTests
{
    [Fact]
    public void DefaultProcessCreatesFrameTimeFromGodotDelta()
    {
        IGodotGameLoopDriver driver = new RecordingGodotDriver();

        driver.Process(0.016);

        var time = Assert.Single(((RecordingGodotDriver)driver).Frames);
        Assert.Equal(0, time.Frame);
        Assert.Equal(TimeSpan.FromSeconds(0.016), time.DeltaTime);
        Assert.Equal(TimeSpan.FromSeconds(0.016), time.RealDeltaTime);
    }

    [Fact]
    public async Task GodotAssetServiceKeepsRuntimeAssetContract()
    {
        var assets = new TestGodotAssetService();
        assets.Add("res://player.tscn", new object());

        using var handle = await assets.LoadAsync<object>("res://player.tscn");

        Assert.IsAssignableFrom<IAssetService>(assets);
        Assert.Equal("res://player.tscn", handle.Location);
        Assert.Equal(1, handle.ReferenceCount);
    }

    [Fact]
    public async Task GodotSceneServiceKeepsRuntimeSceneContract()
    {
        var scenes = new TestGodotSceneService();

        using var handle = await scenes.LoadSceneAsync("res://main.tscn", SceneLoadMode.Additive);

        Assert.IsAssignableFrom<ISceneService>(scenes);
        Assert.Equal("res://main.tscn", handle.Location);
        Assert.Equal(("res://main.tscn", SceneLoadMode.Additive), scenes.Loaded.Single());
    }

    private sealed class RecordingGodotDriver : IGodotGameLoopDriver
    {
        public List<GameFrameTime> Frames { get; } = [];

        public void Process(in GameFrameTime time)
        {
            Frames.Add(time);
        }
    }

    private sealed class TestGodotAssetService : IGodotAssetService
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

    private sealed class TestGodotSceneService : IGodotSceneService
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
