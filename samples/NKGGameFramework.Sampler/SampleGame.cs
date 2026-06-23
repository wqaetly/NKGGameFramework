using NKGGameFramework.Core;
using NKGGameFramework.Ecs;
using NKGGameFramework.Serialization;
using System.Globalization;

namespace NKGGameFramework.Sampler;

internal sealed class SampleGame : IDisposable
{
    // RuntimeContext 是一个可实例化的框架上下文，不依赖全局静态单例。
    // 真实项目可以按游戏实例、服务器房间或测试用例创建多个上下文。
    private readonly RuntimeContext _runtime = new();
    private readonly ProcedureModule _procedures;
    private readonly OdinGameSerializer _serializer = new();

    // World/Scene 是 ECS 的运行边界。这里用一个 battle scene 演示玩法逻辑。
    private World? _world;
    private Scene? _battleScene;
    private Entity _player;
    private bool _disposed;

    public SampleGame()
    {
        // ProcedureModule 负责把游戏从启动、加载、玩法、存档到退出串起来。
        // 它本身也是 RuntimeContext 中的一个普通模块。
        _procedures = _runtime.RegisterModule(new ProcedureModule());
        _procedures.Initialize(
            new BootProcedure(this),
            new LoadProcedure(this),
            new GameplayProcedure(this),
            new SaveProcedure(this),
            new ExitProcedure(this));
    }

    public bool IsRunning { get; private set; }

    public int Frame { get; set; }

    public GameConfig Config { get; private set; } = new("SampleHero", MaxFrames: 3);

    public void Start()
    {
        IsRunning = true;

        // 从 BootProcedure 开始，后续流程切换由各 Procedure 自己决定。
        _procedures.StartProcedure<BootProcedure>();
    }

    public void Update(double deltaTime, double realDeltaTime)
    {
        // 宿主每帧只需要驱动 RuntimeContext；具体模块和 Procedure 会按顺序更新。
        _runtime.Update(deltaTime, realDeltaTime);
    }

    public void LoadGameConfig()
    {
        Config = new GameConfig("SampleHero", MaxFrames: 3);
        Log($"config loaded: player={Config.PlayerName}, maxFrames={Config.MaxFrames}");
    }

    public void CreateBattleScene()
    {
        _world = new World("sample-world");
        _battleScene = _world.CreateScene("battle");

        // SystemGroup 按系统顺序驱动 ECS 逻辑。
        // PresentationBindingSystem 展示组件添加回调，后两个系统展示查询式更新。
        _battleScene.Systems.Add(new PresentationBindingSystem(this));
        _battleScene.Systems.Add(new MovementSystem());
        _battleScene.Systems.Add(new DamageOverTimeSystem());

        // Entity 通过组合组件获得能力；这里没有继承层级，也没有引擎对象依赖。
        _player = _battleScene.CreateEntity()
            .Add(new PlayerTag())
            .Add(new Position(0, 0))
            .Add(new Velocity(2, 0.5))
            .Add(new Health(10));

        Log("battle scene created");
    }

    public void UpdateBattle(double deltaTime, double realDeltaTime)
    {
        // World.Update 会继续驱动它持有的 Scene。
        _world?.Update(deltaTime, realDeltaTime);
    }

    public void SaveSnapshot()
    {
        var snapshot = CreateSnapshot();

        // OdinGameSerializer 默认使用 Odin binary + Base64 字符串接口。
        // 同一个 serializer 也支持 Odin JSON，见 IJsonGameSerializer。
        var payload = _serializer.Serialize(snapshot);
        var restored = _serializer.Deserialize<GameSnapshot>(payload);

        Log($"snapshot saved: chars={payload.Length}, restored={FormatSnapshot(restored)}");
    }

    public void RequestExit()
    {
        IsRunning = false;
    }

    public void Shutdown()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _world?.Dispose();
        _runtime.Dispose();
        _disposed = true;
        Log("game shutdown complete");
    }

    public void Log(string message)
    {
        Console.WriteLine($"[Sample] {message}");
    }

    private GameSnapshot CreateSnapshot()
    {
        // Entity.Get<T> 返回组件引用，可直接读取当前 ECS 状态生成存档 DTO。
        ref var position = ref _player.Get<Position>();
        ref var health = ref _player.Get<Health>();

        return new GameSnapshot
        {
            Frame = Frame,
            PlayerName = Config.PlayerName,
            PositionX = position.X,
            PositionY = position.Y,
            Health = health.Value,
        };
    }

    private static string FormatSnapshot(GameSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "<null>";
        }

        var x = snapshot.PositionX.ToString("0.##", CultureInfo.InvariantCulture);
        var y = snapshot.PositionY.ToString("0.##", CultureInfo.InvariantCulture);
        return $"frame={snapshot.Frame}, player={snapshot.PlayerName}, position=({x}, {y}), health={snapshot.Health}";
    }
}
