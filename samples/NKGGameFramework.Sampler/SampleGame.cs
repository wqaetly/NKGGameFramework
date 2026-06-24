using NKGGameFramework.Core;
using NKGGameFramework.Ecs;
using NKGGameFramework.Serialization;
using System.Globalization;

namespace NKGGameFramework.Sampler;

internal sealed class SampleGame : IDisposable
{
    // 运行时上下文可以按需实例化，不依赖全局静态单例。
    // 真实项目可以按游戏实例、服务器房间或测试用例创建多个上下文。
    private readonly RuntimeContext _runtime = new();
    private readonly ProcedureModule _procedures;
    private readonly OdinGameSerializer _serializer = new();

    // 世界和场景是实体组件系统的运行边界。这里用一个战斗场景演示玩法逻辑。
    private World? _world;
    private Scene? _battleScene;
    private Entity _player;
    private bool _disposed;

    public SampleGame()
    {
        // 流程模块负责把游戏从启动、加载、玩法、存档到退出串起来。
        // 它本身也是运行时上下文中的一个普通模块。
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

    public GameConfig Config { get; private set; } = new("示例英雄", MaxFrames: 3);

    public void Start()
    {
        IsRunning = true;

        // 从启动流程开始，后续流程切换由各流程自己决定。
        _procedures.StartProcedure<BootProcedure>();
    }

    public void Update(double deltaTime, double realDeltaTime)
    {
        // 宿主每帧只需要驱动运行时上下文；具体模块和流程会按顺序更新。
        var time = GameFrameTime.Advance(_runtime.Time, deltaTime, realDeltaTime);
        Update(in time);
    }

    public void Update(in GameFrameTime time)
    {
        // 宿主每帧只需要驱动运行时上下文；具体模块和流程会按顺序更新。
        _runtime.Update(in time);
    }

    public void LoadGameConfig()
    {
        Config = new GameConfig("示例英雄", MaxFrames: 3);
        Log($"配置加载完成：玩家={Config.PlayerName}，最大帧数={Config.MaxFrames}");
    }

    public void CreateBattleScene()
    {
        _world = new World("sample-world");
        _battleScene = _world.CreateScene("battle");

        // 系统组按顺序驱动实体组件系统逻辑。
        // 表现绑定系统展示组件添加回调，后两个系统展示查询式更新。
        _battleScene.Systems.Add(new PresentationBindingSystem(this));
        _battleScene.Systems.Add(new MovementSystem());
        _battleScene.Systems.Add(new DamageOverTimeSystem());

        // 实体通过组合组件获得能力；这里没有继承层级，也没有引擎对象依赖。
        _player = _battleScene.CreateEntity()
            .Add(new PlayerTag())
            .Add(new Position(0, 0))
            .Add(new Velocity(2, 0.5))
            .Add(new Health(10));

        Log("战斗场景创建完成");
    }

    public void UpdateBattle(double deltaTime, double realDeltaTime)
    {
        // 世界更新会继续驱动它持有的场景。
        var time = _world is null
            ? GameFrameTime.FromSeconds(deltaTime, realDeltaTime)
            : GameFrameTime.Advance(_world.Time, deltaTime, realDeltaTime);
        UpdateBattle(in time);
    }

    public void UpdateBattle(in GameFrameTime time)
    {
        // 世界更新会继续驱动它持有的场景。
        _world?.Update(in time);
    }

    public void SaveSnapshot()
    {
        var snapshot = CreateSnapshot();

        // 序列化器默认使用二进制转字符串接口。
        // 同一个序列化器也支持文本格式，适合调试和配置检查。
        var payload = _serializer.Serialize(snapshot);
        var restored = _serializer.Deserialize<GameSnapshot>(payload);

        Log($"快照保存完成：字符数={payload.Length}，还原结果={FormatSnapshot(restored)}");
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
        Log("游戏关闭完成");
    }

    public void Log(string message)
    {
        Console.WriteLine($"[基础示例] {message}");
    }

    private GameSnapshot CreateSnapshot()
    {
        // 实体组件读取会返回组件引用，可直接读取当前实体组件状态生成存档对象。
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
            return "<空>";
        }

        var x = snapshot.PositionX.ToString("0.##", CultureInfo.InvariantCulture);
        var y = snapshot.PositionY.ToString("0.##", CultureInfo.InvariantCulture);
        return $"帧={snapshot.Frame}，玩家={snapshot.PlayerName}，位置=({x}, {y})，生命={snapshot.Health}";
    }
}
