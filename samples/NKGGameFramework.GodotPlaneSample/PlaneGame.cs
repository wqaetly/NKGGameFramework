using NKGGameFramework.Adapter.Godot;
using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.GodotPlaneSample;

internal sealed class PlaneGame : IDisposable
{
    private readonly RuntimeContext _runtime = new();
    private readonly World _world = new("godot-plane-world");
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
        commands.BeginFrame(state.Frame, state.Score, state.Lives, IsGameOver);

        AppendEntity(commands, "PLAYER", _player, ReadPosition(_player));

        _scene.Query<EnemyTag, Position>().ForEach((ref EnemyTag _, ref Position position, Entity entity) =>
        {
            AppendEntity(commands, "ENEMY", entity, position);
        });

        _scene.Query<BulletTag, Position>().ForEach((ref BulletTag _, ref Position position, Entity entity) =>
        {
            AppendEntity(commands, "BULLET", entity, position);
        });

        return commands;
    }

    public void Dispose()
    {
        _world.Dispose();
        _runtime.Dispose();
    }

    private static void AppendEntity(GodotHostCommandBuffer commands, string kind, Entity entity, Position position)
    {
        commands.UpsertNode2D(kind, entity.Id.Value, position.X, position.Y);
    }

    private static Position ReadPosition(Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        return position;
    }
}
