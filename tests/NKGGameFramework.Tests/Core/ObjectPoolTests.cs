using NKGGameFramework.Core;

namespace NKGGameFramework.Core.Tests;

public sealed class ObjectPoolTests
{
    [Fact]
    public void ObjectPoolSpawnsUnspawnsAndReleasesExpiredObjects()
    {
        var pool = new ObjectPool<TestObject>("effects", capacity: 2, expireAfter: TimeSpan.Zero);
        var fx = new TestObject("slash");

        pool.Register(fx);
        var spawned = pool.Spawn("slash");
        pool.Unspawn(fx.Target);
        var released = pool.ReleaseExpired(DateTimeOffset.UtcNow);

        Assert.Same(fx, spawned);
        Assert.Equal(1, fx.Spawned);
        Assert.Equal(1, fx.Unspawned);
        Assert.Equal(1, fx.Released);
        Assert.Equal(1, released);
    }

    private sealed class TestObject(string name) : PoolObject
    {
        public override string Name { get; } = name;

        public override object Target { get; } = new object();

        public int Spawned { get; private set; }

        public int Unspawned { get; private set; }

        public int Released { get; private set; }

        protected override void OnSpawn() => Spawned++;

        protected override void OnUnspawn() => Unspawned++;

        protected override void OnRelease() => Released++;
    }
}
