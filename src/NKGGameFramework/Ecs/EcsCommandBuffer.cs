using NKGGameFramework.Core;

namespace NKGGameFramework.Ecs;

public sealed class EcsCommandBuffer : IPoolItem, IDisposable
{
    private readonly List<ICommand> _commands = [];
    private MemoryPool<EcsCommandBuffer>? _owner;
    private Scene? _scene;
    private bool _playedBack;
    private bool _released = true;

    internal void Initialize(Scene scene, MemoryPool<EcsCommandBuffer> owner)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(owner);

        _scene = scene;
        _owner = owner;
        _playedBack = false;
        _released = false;
    }

    public int Count => _commands.Count;

    public void OnAcquire()
    {
    }

    public void OnRelease()
    {
        ReleaseCommands();
        _scene = null;
        _owner = null;
        _playedBack = false;
        _released = true;
    }

    public void Add<TComponent>(Entity entity, TComponent component)
        where TComponent : struct, IComponent
    {
        EnsureNotPlayedBack();
        _commands.Add(AddComponentCommand<TComponent>.Rent(entity.ToRef(), component));
    }

    public void Remove<TComponent>(Entity entity)
        where TComponent : struct, IComponent
    {
        EnsureNotPlayedBack();
        _commands.Add(RemoveComponentCommand<TComponent>.Rent(entity.ToRef()));
    }

    public void Destroy(Entity entity)
    {
        EnsureNotPlayedBack();
        _commands.Add(DestroyEntityCommand.Rent(entity.ToRef()));
    }

    public void Playback()
    {
        var scene = GetScene();
        EnsureNotPlayedBack();

        if (scene.IsQueryActive)
        {
            throw new InvalidOperationException("Command buffers must be played back outside active ECS query loops.");
        }

        try
        {
            foreach (var command in _commands)
            {
                command.Apply(scene);
            }

            _playedBack = true;
        }
        finally
        {
            ReleaseCommands();
            ReleaseToPool();
        }
    }

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        ReleaseCommands();
        ReleaseToPool();
    }

    private void EnsureNotPlayedBack()
    {
        if (_released)
        {
            throw new InvalidOperationException("Command buffer has already been released.");
        }

        if (_playedBack)
        {
            throw new InvalidOperationException("Command buffer has already been played back.");
        }
    }

    private Scene GetScene()
    {
        EnsureNotPlayedBack();
        return _scene!;
    }

    private void ReleaseToPool()
    {
        var owner = _owner;
        if (owner is null)
        {
            _released = true;
            return;
        }

        owner.Release(this);
    }

    private void ReleaseCommands()
    {
        foreach (var command in _commands)
        {
            command.ReleaseToPool();
        }

        _commands.Clear();
    }

    private interface ICommand : IPoolItem
    {
        void Apply(Scene scene);

        void ReleaseToPool();
    }

    private sealed class AddComponentCommand<TComponent> : ICommand
        where TComponent : struct, IComponent
    {
        private static readonly MemoryPool<AddComponentCommand<TComponent>> Pool = new(static () => new AddComponentCommand<TComponent>());

        private EntityRef _entity;
        private TComponent _component;

        public static AddComponentCommand<TComponent> Rent(EntityRef entity, TComponent component)
        {
            var command = Pool.Acquire();
            command._entity = entity;
            command._component = component;
            return command;
        }

        public void OnAcquire()
        {
        }

        public void OnRelease()
        {
            _entity = default;
            _component = default;
        }

        public void Apply(Scene scene)
        {
            if (_entity.TryGet(out var entity))
            {
                entity.Add(_component);
            }
        }

        public void ReleaseToPool()
        {
            Pool.Release(this);
        }
    }

    private sealed class RemoveComponentCommand<TComponent> : ICommand
        where TComponent : struct, IComponent
    {
        private static readonly MemoryPool<RemoveComponentCommand<TComponent>> Pool = new(static () => new RemoveComponentCommand<TComponent>());

        private EntityRef _entity;

        public static RemoveComponentCommand<TComponent> Rent(EntityRef entity)
        {
            var command = Pool.Acquire();
            command._entity = entity;
            return command;
        }

        public void OnAcquire()
        {
        }

        public void OnRelease()
        {
            _entity = default;
        }

        public void Apply(Scene scene)
        {
            if (_entity.TryGet(out var entity))
            {
                entity.Remove<TComponent>();
            }
        }

        public void ReleaseToPool()
        {
            Pool.Release(this);
        }
    }

    private sealed class DestroyEntityCommand : ICommand
    {
        private static readonly MemoryPool<DestroyEntityCommand> Pool = new(static () => new DestroyEntityCommand());

        private EntityRef _entity;

        public static DestroyEntityCommand Rent(EntityRef entity)
        {
            var command = Pool.Acquire();
            command._entity = entity;
            return command;
        }

        public void OnAcquire()
        {
        }

        public void OnRelease()
        {
            _entity = default;
        }

        public void Apply(Scene scene)
        {
            if (_entity.TryGet(out var entity))
            {
                entity.Destroy();
            }
        }

        public void ReleaseToPool()
        {
            Pool.Release(this);
        }
    }
}
