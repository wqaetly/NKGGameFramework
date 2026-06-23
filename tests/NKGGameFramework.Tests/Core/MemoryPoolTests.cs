using NKGGameFramework.Core;

namespace NKGGameFramework.Core.Tests;

public sealed class MemoryPoolTests
{
    [Fact]
    public void AcquireReleaseReuseAndDoubleReleaseIsRejected()
    {
        var pool = new MemoryPool<PooledCommand>();

        var first = pool.Acquire();
        first.Value = 42;
        pool.Release(first);
        var reused = pool.Acquire();

        Assert.Same(first, reused);
        Assert.Equal(2, reused.AcquireCount);
        Assert.Equal(0, reused.Value);

        pool.Release(reused);
        Assert.Throws<InvalidOperationException>(() => pool.Release(reused));
    }

    [Fact]
    public void FactoryCanCreateItemsWithoutPublicConstructor()
    {
        var pool = new MemoryPool<FactoryOnlyItem>(static () => new FactoryOnlyItem(7));

        var item = pool.Acquire();

        Assert.Equal(7, item.Value);
        pool.Release(item);
    }

    private sealed class PooledCommand : IPoolItem
    {
        public int AcquireCount { get; private set; }

        public int Value { get; set; }

        public void OnAcquire()
        {
            AcquireCount++;
        }

        public void OnRelease()
        {
            Value = 0;
        }
    }

    private sealed class FactoryOnlyItem : IPoolItem
    {
        internal FactoryOnlyItem(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public void OnAcquire()
        {
        }

        public void OnRelease()
        {
        }
    }
}
