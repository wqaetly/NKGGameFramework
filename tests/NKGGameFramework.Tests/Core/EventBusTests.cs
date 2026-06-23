using NKGGameFramework.Core;

namespace NKGGameFramework.Core.Tests;

public sealed class EventBusTests
{
    [Fact]
    public void SubscribePublishAndDisposeAreScopedByEventType()
    {
        var bus = new EventBus();
        var count = 0;

        using var subscription = bus.Subscribe<DamageEvent>(evt => count += evt.Amount);

        bus.Publish(new DamageEvent(3));
        bus.Publish(new NameChangedEvent("ignored"));
        subscription.Dispose();
        bus.Publish(new DamageEvent(10));

        Assert.Equal(3, count);
    }

    [Fact]
    public void DuplicateSubscribeThrowsAndUnsubscribeReturnsStatus()
    {
        var bus = new EventBus();
        Action<DamageEvent> handler = _ => { };

        using var subscription = bus.Subscribe(handler);

        Assert.True(bus.HasHandler(handler));
        Assert.Throws<InvalidOperationException>(() => bus.Subscribe(handler));
        Assert.True(bus.Unsubscribe(handler));
        Assert.False(bus.Unsubscribe(handler));
        Assert.False(bus.HasHandler<DamageEvent>());
    }

    [Fact]
    public void FireQueuesUntilDispatch()
    {
        var bus = new EventBus();
        var count = 0;

        bus.Subscribe<DamageEvent>(evt => count += evt.Amount);
        bus.Fire(new DamageEvent(4));

        Assert.Equal(0, count);
        Assert.Equal(1, bus.QueuedEventCount);
        Assert.Equal(1, bus.DispatchQueuedEvents());
        Assert.Equal(4, count);
        Assert.Equal(0, bus.QueuedEventCount);
    }

    [Fact]
    public void RuntimeContextDispatchesQueuedEventsAfterModuleUpdates()
    {
        using var context = new RuntimeContext();
        var calls = new List<string>();

        context.Events.Subscribe<DamageEvent>(evt => calls.Add($"event:{evt.Amount}"));
        context.RegisterModule(new EventRecordingModule(calls));
        context.Events.Fire(new DamageEvent(9));

        context.Update(0.016, 0.016);

        Assert.Equal(["module", "event:9"], calls);
    }

    [Fact]
    public void ContinueExceptionPolicyInvokesRemainingHandlers()
    {
        var bus = new EventBus
        {
            ExceptionPolicy = EventExceptionPolicy.Continue,
        };

        var handled = false;
        var errors = new List<string>();
        bus.ExceptionHandler = (exception, eventType) => errors.Add($"{eventType.Name}:{exception.Message}");

        bus.Subscribe<DamageEvent>(_ => throw new InvalidOperationException("broken"));
        bus.Subscribe<DamageEvent>(_ => handled = true);

        bus.FireNow(new DamageEvent(1));

        Assert.True(handled);
        Assert.Equal(["DamageEvent:broken"], errors);
    }

    [Fact]
    public void PooledEventArgsAreReleasedAfterQueuedDispatch()
    {
        var bus = new EventBus();
        var count = 0;

        bus.Subscribe<PooledDamageEvent>(evt => count += evt.Amount);

        var evt = bus.Rent<PooledDamageEvent>();
        evt.Amount = 7;
        bus.FirePooled(evt);
        bus.DispatchQueuedEvents();

        var reused = bus.Rent<PooledDamageEvent>();

        Assert.Same(evt, reused);
        Assert.Equal(7, count);
        Assert.Equal(0, reused.Amount);

        bus.Return(reused);
    }

    private sealed record DamageEvent(int Amount);

    private sealed record NameChangedEvent(string Name);

    private sealed class EventRecordingModule(List<string> calls) : Module, IUpdateModule
    {
        public void Update(double deltaTime, double realDeltaTime)
        {
            calls.Add("module");
        }
    }

    private sealed class PooledDamageEvent : GameEventArgs
    {
        public int Amount { get; set; }

        public override void Clear()
        {
            Amount = 0;
        }
    }
}
