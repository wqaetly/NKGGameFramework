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
    public void AssetHandleRejectsRetainAndReleaseAfterDisposed()
    {
        var releases = 0;
        var handle = new AssetHandle<string>("hero/avatar", "asset", _ => releases++);

        handle.Dispose();

        Assert.Equal(0, handle.ReferenceCount);
        Assert.Equal(1, releases);
        Assert.Throws<ObjectDisposedException>(() => handle.Retain());
        Assert.Throws<ObjectDisposedException>(() => handle.Release());
    }

    [Fact]
    public void SceneHandleUnloadsOnce()
    {
        var unloaded = new List<string>();
        using var handle = new SceneHandle("scenes/battle", scene => unloaded.Add(scene.Location));

        Assert.Equal("scenes/battle", handle.Location);

        handle.Dispose();
        handle.Dispose();

        Assert.Equal(["scenes/battle"], unloaded);
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
    public async Task RuntimeServicesRoundTripCoreHostContracts()
    {
        var scenes = new InMemorySceneService();
        var ui = new InMemoryUIService();
        var audio = new InMemoryAudioService();
        var configs = new InMemoryConfigService();
        var localization = new InMemoryLocalizationService("zh-CN");
        var presentation = new InMemoryPresentationService();
        var logicalEntity = new object();
        configs.Add("player", new PlayerConfig(100));
        localization.Add("menu.start", "Start");

        using var scene = await scenes.LoadSceneAsync("scenes/battle", SceneLoadMode.Additive);
        using var view = await ui.OpenAsync("ui/hud");
        using var music = await audio.PlayAsync("audio/battle", new AudioPlaybackOptions(0.5f, Loop: true));
        var config = await configs.LoadAsync<PlayerConfig>("player");
        var text = localization.GetText("menu.start");
        var entityView = await presentation.BindAsync(logicalEntity, "views/player");

        Assert.Equal("scenes/battle", scene.Location);
        Assert.Equal(("scenes/battle", SceneLoadMode.Additive), scenes.LoadedScenes.Single());
        Assert.Equal("ui/hud", view.Location);
        Assert.Equal("audio/battle", music.Location);
        Assert.Equal(("audio/battle", new AudioPlaybackOptions(0.5f, Loop: true)), audio.Played.Single());
        Assert.Equal(new PlayerConfig(100), config);
        Assert.Equal("zh-CN", localization.CurrentCulture);
        Assert.Equal("Start", text);
        Assert.Equal(1, entityView.ViewId);
        Assert.Equal((logicalEntity, "views/player"), presentation.Bindings.Single());

        scene.Dispose();
        view.Dispose();
        music.Dispose();

        Assert.Equal(["scenes/battle"], scenes.UnloadedScenes);
        Assert.Equal(["ui/hud"], ui.ClosedViews);
        Assert.Equal(["audio/battle"], audio.StoppedAudio);
    }

    [Fact]
    public async Task RuntimeServicesPropagateCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var assets = new InMemoryAssetService();
        var scenes = new InMemorySceneService();
        var ui = new InMemoryUIService();
        var audio = new InMemoryAudioService();
        var configs = new InMemoryConfigService();
        var presentation = new InMemoryPresentationService();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await assets.LoadAsync<string>("missing", cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await scenes.LoadSceneAsync("scene", SceneLoadMode.Single, cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await ui.OpenAsync("view", cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await audio.PlayAsync("music", cancellationToken: cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await configs.LoadAsync<PlayerConfig>("player", cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await presentation.BindAsync(new object(), "view", cts.Token));
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

    private sealed class InMemorySceneService : ISceneService
    {
        public List<(string Location, SceneLoadMode Mode)> LoadedScenes { get; } = [];

        public List<string> UnloadedScenes { get; } = [];

        public UniTask<ISceneHandle> LoadSceneAsync(string location, SceneLoadMode mode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadedScenes.Add((location, mode));
            return GameAsync.FromResult<ISceneHandle>(new SceneHandle(location, scene => UnloadedScenes.Add(scene.Location)));
        }
    }

    private sealed class InMemoryUIService : IUIService
    {
        public List<string> ClosedViews { get; } = [];

        public UniTask<IViewHandle> OpenAsync(string location, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GameAsync.FromResult<IViewHandle>(new TestViewHandle(location, ClosedViews));
        }
    }

    private sealed class InMemoryAudioService : IAudioService
    {
        public List<(string Location, AudioPlaybackOptions Options)> Played { get; } = [];

        public List<string> StoppedAudio { get; } = [];

        public UniTask<IAudioHandle> PlayAsync(
            string location,
            AudioPlaybackOptions options = default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Played.Add((location, options));
            return GameAsync.FromResult<IAudioHandle>(new TestAudioHandle(location, StoppedAudio));
        }
    }

    private sealed class InMemoryConfigService : IConfigService
    {
        private readonly Dictionary<string, object> _configs = [];

        public void Add<TConfig>(string key, TConfig config)
            where TConfig : class
        {
            _configs.Add(key, config);
        }

        public UniTask<TConfig> LoadAsync<TConfig>(string key, CancellationToken cancellationToken = default)
            where TConfig : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GameAsync.FromResult((TConfig)_configs[key]);
        }
    }

    private sealed class InMemoryLocalizationService(string currentCulture) : ILocalizationService
    {
        private readonly Dictionary<string, string> _texts = [];

        public string CurrentCulture { get; } = currentCulture;

        public void Add(string key, string value)
        {
            _texts.Add(key, value);
        }

        public string GetText(string key) => _texts[key];
    }

    private sealed class InMemoryPresentationService : IPresentationService
    {
        private long _nextViewId = 1;

        public List<(object Entity, string ViewLocation)> Bindings { get; } = [];

        public UniTask<IEntityView> BindAsync(object logicalEntity, string viewLocation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Bindings.Add((logicalEntity, viewLocation));
            return GameAsync.FromResult<IEntityView>(new TestEntityView(_nextViewId++));
        }
    }

    private sealed record PlayerConfig(int MaxHealth);

    private sealed class TestViewHandle(string location, List<string> closedViews) : IViewHandle
    {
        private bool _disposed;

        public string Location { get; } = location;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            closedViews.Add(Location);
        }
    }

    private sealed class TestAudioHandle(string location, List<string> stoppedAudio) : IAudioHandle
    {
        private bool _disposed;

        public string Location { get; } = location;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            stoppedAudio.Add(Location);
        }
    }

    private sealed record TestEntityView(long ViewId) : IEntityView;
}
