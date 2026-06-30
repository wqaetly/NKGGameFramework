using NKGGameFramework.Adapter.Godot;
using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.GodotPlaneSample;

internal sealed class PlaneGame : IDisposable
{
    private const double DisplayScale = 2.0d;
    private const int HudObjectId = 2_000_000_001;

    private readonly RuntimeContext _runtime = new();
    private readonly World _world = new("godot-plane-world");
    private readonly HashSet<int> _visibleObjectIds = new();
    private readonly Scene _scene;
    private Entity _player;

    public PlaneGame()
    {
        _scene = _world.CreateScene("air-space");
        _scene.GetOrCreateSceneComponent(static () => new PlaneGameState());
        _scene.GetOrCreateSceneComponent(static () => new PlaneInputState());
        _scene.Systems.Add(new PlayerInputSystem());
        _scene.Systems.Add(new EnemyPatternSystem());
        _scene.Systems.Add(new EnemySpawnSystem());
        _scene.Systems.Add(new BulletSpawnerSystem());
        _scene.Systems.Add(new MovementSystem());
        _scene.Systems.Add(new CollisionSystem());
    }

    public int Score => State.Score;

    public int Lives => State.Lives;

    public bool IsGameOver => State.Lives <= 0 || State.Frame >= 10800;

    public double PlayerX => ReadPosition(_player).X;

    public int BulletCount
    {
        get
        {
            var count = 0;
            _scene.Query<BulletTag>().ForEach((ref BulletTag _, Entity __) => count++);
            return count;
        }
    }

    public int VisualObjectCount
    {
        get
        {
            var count = 1;
            _scene.Query<EnemyTag>().ForEach((ref EnemyTag _, Entity __) => count++);
            _scene.Query<BulletTag>().ForEach((ref BulletTag _, Entity __) => count++);
            return count;
        }
    }

    public string HostStatus { get; set; } = "boot";

    public int DebugPort { get; set; }

    public RuntimeContext Runtime => _runtime;

    public World World => _world;

    private PlaneGameState State => _scene.GetOrCreateSceneComponent<PlaneGameState>();

    public void Start()
    {
        _player = _scene.CreateEntity()
            .Add(new PlayerTag())
            .Add(new Position(PlaneGameRules.ArenaWidth * 0.5d, PlaneGameRules.ArenaHeight - 42))
            .Add(new Velocity(0, 0))
            .Add(new Bounds(17));

        var state = State;
        for (var i = 0; i < PlaneGameRules.TargetEnemyCount; i++)
        {
            PlaneGameRules.SpawnEnemy(_scene, state, -24 - i * 44);
        }
    }

    public void SetInput(int moveX, int moveY, bool fire)
    {
        var input = _scene.GetOrCreateSceneComponent<PlaneInputState>();
        input.MoveX = moveX;
        input.MoveY = moveY;
        input.Fire = fire;
    }

    public void Update(double deltaSeconds)
    {
        var previousRuntimeFrame = _runtime.Time.Frame;
        var runtimeTime = GameFrameTime.Advance(_runtime.Time, deltaSeconds, deltaSeconds);
        _runtime.Update(in runtimeTime);
        if (_runtime.Time.Frame == previousRuntimeFrame)
        {
            return;
        }

        var worldTime = GameFrameTime.Advance(_world.Time, deltaSeconds, deltaSeconds);
        _world.Update(in worldTime);
    }

    public string CreateSnapshot()
    {
        return CreateCommandBuffer().Build();
    }

    public byte[] CreateCommandBytes()
    {
        return CreateCommandBuffer().BuildBytes();
    }

    public GodotHostCommandBuffer CreateCommandBuffer()
    {
        var state = State;
        var commands = new GodotHostCommandBuffer();
        var host = new GodotHostCommands(commands);
        var nextVisibleObjectIds = new HashSet<int>();

        commands.BeginFrame(state.Frame, state.Score, state.Lives, IsGameOver);

        AppendHud(host);
        AppendEntity(host, nextVisibleObjectIds, "PLAYER", _player, ReadPosition(_player));

        _scene.Query<EnemyTag, Position>().ForEach((ref EnemyTag _, ref Position position, Entity entity) =>
        {
            AppendEntity(host, nextVisibleObjectIds, "ENEMY", entity, position);
        });

        _scene.Query<BulletTag, Position>().ForEach((ref BulletTag _, ref Position position, Entity entity) =>
        {
            AppendEntity(host, nextVisibleObjectIds, "BULLET", entity, position);
        });

        foreach (var objectId in _visibleObjectIds)
        {
            if (!nextVisibleObjectIds.Contains(objectId))
            {
                host.GetNode(new GodotObjectId(objectId)).Destroy();
            }
        }

        _visibleObjectIds.Clear();
        foreach (var objectId in nextVisibleObjectIds)
        {
            _visibleObjectIds.Add(objectId);
        }

        return commands;
    }

    public void Dispose()
    {
        _world.Dispose();
        _runtime.Dispose();
    }

    private void AppendEntity(GodotHostCommands host, HashSet<int> nextVisibleObjectIds, string kind, Entity entity, Position position)
    {
        var objectId = checked((int)entity.Id.Value);
        nextVisibleObjectIds.Add(objectId);

        var node = _visibleObjectIds.Contains(objectId)
            ? host.GetNode(new GodotObjectId(objectId))
            : host.CreateNode(objectId, "Polygon2D", $"{kind}_{objectId}");
        if (!_visibleObjectIds.Contains(objectId))
        {
            node.SetParent(GodotObjectId.Root);
            node.SetProperty("polygon", GodotVariant.FromPackedVector2Array(PolygonForKind(kind)));
            node.SetProperty("color", GodotVariant.FromColor(ColorForKind(kind)));
        }

        node.SetTransform2D(position.X * DisplayScale, position.Y * DisplayScale);
        node.SetVisible(true);
    }

    private void AppendHud(GodotHostCommands host)
    {
        var node = host.CreateNode(HudObjectId, "Label", "Hud");
        node.SetParent(GodotObjectId.Root);
        node.SetTransform2D(14, 10);
        node.SetProperty("text", GodotVariant.FromString(
            "Controls: arrows move  Space/Enter fire\n" +
            $"LeanCLR {HostStatus}\n" +
            $"WebDebug http://127.0.0.1:{DebugPort}\n" +
            $"score {Score}  lives {Lives}  enemies/bullets {VisualObjectCount}"));
        node.SetVisible(true);
    }

    private static Position ReadPosition(Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        return position;
    }

    private static GodotColor ColorForKind(string kind)
    {
        return kind switch
        {
            "PLAYER" => new GodotColor(0.2, 0.78, 0.95),
            "ENEMY" => new GodotColor(0.96, 0.25, 0.24),
            _ => new GodotColor(1.0, 0.93, 0.36)
        };
    }

    private static GodotVector2[] PolygonForKind(string kind)
    {
        return kind switch
        {
            "PLAYER" =>
            [
                new GodotVector2(0, -36),
                new GodotVector2(-10, 8),
                new GodotVector2(-30, 28),
                new GodotVector2(-6, 20),
                new GodotVector2(0, 12),
                new GodotVector2(6, 20),
                new GodotVector2(30, 28),
                new GodotVector2(10, 8)
            ],
            "ENEMY" =>
            [
                new GodotVector2(-28, -16),
                new GodotVector2(28, -16),
                new GodotVector2(34, 8),
                new GodotVector2(12, 24),
                new GodotVector2(0, 16),
                new GodotVector2(-12, 24),
                new GodotVector2(-34, 8)
            ],
            _ =>
            [
                new GodotVector2(0, -14),
                new GodotVector2(6, 0),
                new GodotVector2(0, 14),
                new GodotVector2(-6, 0)
            ]
        };
    }
}
