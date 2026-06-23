using NKGGameFramework.Ecs;

namespace NKGGameFramework.Ecs.Tests;

public sealed class EntityRefTests
{
    [Fact]
    public void EntityRefBecomesInvalidAfterEntityIsDestroyed()
    {
        var scene = new Scene("battle");
        var entity = scene.CreateEntity();
        var reference = entity.ToRef();

        Assert.True(reference.IsAlive);
        Assert.True(reference.TryGet(out var resolved));
        Assert.Equal(entity, resolved);

        entity.Destroy();

        Assert.False(reference.IsAlive);
        Assert.False(reference.TryGet(out _));
    }

    [Fact]
    public void EntityRefDoesNotResolveReusedEntityIdWithNewVersion()
    {
        var scene = new Scene("reuse");
        var first = scene.CreateEntity();
        var firstRef = first.ToRef();

        first.Destroy();
        var reused = scene.CreateEntity();

        Assert.Equal(first.Id, reused.Id);
        Assert.NotEqual(first.Version, reused.Version);
        Assert.False(firstRef.TryGet(out _));
    }
}
