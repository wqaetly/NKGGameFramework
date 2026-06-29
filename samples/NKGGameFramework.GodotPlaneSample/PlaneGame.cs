using System.Globalization;
using System.Text;
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
        var runtimeTime = GameFrameTime.Advance(_runtime.Time, deltaSeconds, deltaSeconds);
        _runtime.Update(in runtimeTime);

        var worldTime = GameFrameTime.Advance(_world.Time, deltaSeconds, deltaSeconds);
        _world.Update(in worldTime);
    }

    public string CreateSnapshot()
    {
        var state = State;
        var builder = new StringBuilder(1024);
        builder.Append(CultureInfo.InvariantCulture, $"STATE {state.Frame} {state.Score} {state.Lives} {(IsGameOver ? 1 : 0)}\n");

        AppendEntity(builder, "PLAYER", _player, ReadPosition(_player));

        _scene.Query<EnemyTag, Position>().ForEach((ref EnemyTag _, ref Position position, Entity entity) =>
        {
            AppendEntity(builder, "ENEMY", entity, position);
        });

        _scene.Query<BulletTag, Position>().ForEach((ref BulletTag _, ref Position position, Entity entity) =>
        {
            AppendEntity(builder, "BULLET", entity, position);
        });

        builder.Append("END");
        return builder.ToString();
    }

    public void Dispose()
    {
        _world.Dispose();
        _runtime.Dispose();
    }

    private static void AppendEntity(StringBuilder builder, string kind, Entity entity, Position position)
    {
        builder.Append(CultureInfo.InvariantCulture, $"{kind} {entity.Id.Value} {position.X:0.###} {position.Y:0.###}\n");
    }

    private static Position ReadPosition(Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        return position;
    }
}
