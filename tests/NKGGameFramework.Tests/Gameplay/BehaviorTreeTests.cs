using NKGGameFramework.Core;
using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.Tests.Gameplay;

public sealed class BehaviorTreeTests
{
    [Fact]
    public void Long_immediate_sequence_is_processed_without_recursive_stack_growth()
    {
        using var scene = new Scene("behavior");
        var calls = 0;
        var action = new DelegateBehaviorAction(_ =>
        {
            calls++;
            return BehaviorActionStatus.Success;
        });
        var children = Enumerable.Range(0, 20_000)
            .Select(_ => new BehaviorActionNode(action))
            .ToArray();
        var tree = new BehaviorTreeInstance(
            new BehaviorSequenceNode(children),
            new BehaviorActionRegistry(),
            new BehaviorTreeContext(scene));

        tree.Start();

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
        Assert.Equal(20_000, calls);
    }

    [Fact]
    public void Wait_node_completes_after_elapsed_update_time()
    {
        using var scene = new Scene("behavior");
        var tree = new BehaviorTreeInstance(
            new BehaviorWaitNode(TimeSpan.FromSeconds(1)),
            new BehaviorActionRegistry(),
            new BehaviorTreeContext(scene));

        tree.Start();
        tree.Update(TimeSpan.FromSeconds(0.25));

        Assert.Equal(BehaviorTreeStatus.Running, tree.Status);

        tree.Update(TimeSpan.FromSeconds(0.75));

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
    }

    [Fact]
    public void Wait_node_can_resolve_duration_from_wrapped_blackboard_value()
    {
        using var scene = new Scene("behavior");
        var blackboard = new BehaviorBlackboard(scene);
        blackboard.Set("delay", 10);
        var tree = new BehaviorTreeInstance(
            new BehaviorWaitNode("delay"),
            new BehaviorActionRegistry(),
            blackboard: blackboard);

        tree.Start();
        tree.Update(TimeSpan.FromMilliseconds(9));

        Assert.Equal(BehaviorTreeStatus.Running, tree.Status);

        tree.Update(TimeSpan.FromMilliseconds(1));

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
    }

    [Fact]
    public void Blackboard_value_types_are_wrapped_and_reused()
    {
        using var scene = new Scene("behavior");
        var blackboard = new BehaviorBlackboard(scene);
        var changes = 0;
        BehaviorBlackboardValue? lastRawValue = null;
        blackboard.AddObserver("count", change =>
        {
            changes++;
            lastRawValue = change.Value;
        });

        blackboard.Set("count", 1);

        var raw = Assert.IsType<BehaviorBlackboardValue<int>>(blackboard.Values["count"]);
        Assert.True(blackboard.TryGet<int>("count", out var value));
        Assert.Equal(1, value);
        Assert.Same(raw, lastRawValue);

        blackboard.Set("count", 1);

        Assert.Equal(1, changes);

        blackboard.Set("count", 2);

        Assert.Equal(2, changes);
        Assert.Same(raw, blackboard.Values["count"]);
        Assert.Equal(2, raw.Value);

        blackboard.Unset("count");
        blackboard.Set("count", 3);

        var reused = Assert.IsType<BehaviorBlackboardValue<int>>(blackboard.Values["count"]);
        Assert.Same(raw, reused);
        Assert.Equal(3, reused.Value);
    }

    [Fact]
    public void Default_blackboards_created_with_same_scene_share_value_pool()
    {
        using var scene = new Scene("behavior");
        var context = new BehaviorTreeContext(scene);
        var first = new BehaviorTreeInstance(
            new BehaviorWaitUntilStoppedNode(),
            new BehaviorActionRegistry(),
            context);
        first.Blackboard.Set("count", 1);
        var firstValue = first.Blackboard.Values["count"];
        Assert.True(scene.TryGetSceneComponent<BehaviorBlackboardPoolComponent>(out _));
        first.Blackboard.Unset("count");
        var second = new BehaviorTreeInstance(
            new BehaviorWaitUntilStoppedNode(),
            new BehaviorActionRegistry(),
            context);

        second.Blackboard.Set("count", 2);

        var secondValue = Assert.IsType<BehaviorBlackboardValue<int>>(second.Blackboard.Values["count"]);
        Assert.Same(firstValue, secondValue);
        Assert.Equal(2, secondValue.Value);
    }

    [Fact]
    public void Default_blackboard_requires_scene_context()
    {
        Assert.Throws<InvalidOperationException>(() => new BehaviorTreeInstance(
            new BehaviorWaitUntilStoppedNode(),
            new BehaviorActionRegistry()));
    }

    [Fact]
    public void Blackboard_condition_can_abort_self_when_value_changes()
    {
        using var scene = new Scene("behavior");
        var blackboard = new BehaviorBlackboard(scene);
        blackboard.Set("can_continue", true);
        var tree = new BehaviorTreeInstance(
            new BehaviorBlackboardConditionNode(
                "can_continue",
                BehaviorConditionOperator.Equal,
                BehaviorBlackboardValue.Create(true),
                BehaviorObserverStops.Self,
                new BehaviorWaitUntilStoppedNode()),
            new BehaviorActionRegistry(),
            blackboard: blackboard);

        tree.Start();

        Assert.Equal(BehaviorTreeStatus.Running, tree.Status);

        blackboard.Set("can_continue", false);

        Assert.Equal(BehaviorTreeStatus.Failed, tree.Status);
    }

    [Fact]
    public void Blackboard_condition_compares_wrapped_numeric_values()
    {
        using var scene = new Scene("behavior");
        var blackboard = new BehaviorBlackboard(scene);
        blackboard.Set("count", 3);
        var tree = new BehaviorTreeInstance(
            new BehaviorBlackboardConditionNode(
                "count",
                BehaviorConditionOperator.GreaterOrEqual,
                BehaviorBlackboardValue.Create(2L),
                BehaviorObserverStops.None,
                new BehaviorActionNode(new DelegateBehaviorAction(_ => BehaviorActionStatus.Success))),
            new BehaviorActionRegistry(),
            blackboard: blackboard);

        tree.Start();

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
    }

    [Fact]
    public void Blackboard_condition_can_restart_higher_priority_selector_branch()
    {
        var calls = new List<string>();
        using var scene = new Scene("behavior");
        var blackboard = new BehaviorBlackboard(scene);
        blackboard.Set("ready", false);
        var highPriority = new BehaviorBlackboardConditionNode(
            "ready",
            BehaviorConditionOperator.Equal,
            BehaviorBlackboardValue.Create(true),
            BehaviorObserverStops.ImmediateRestart,
            new BehaviorActionNode(new DelegateBehaviorAction(_ =>
            {
                calls.Add("high");
                return BehaviorActionStatus.Success;
            })));
        var lowPriority = new BehaviorWaitUntilStoppedNode();
        var tree = new BehaviorTreeInstance(
            new BehaviorSelectorNode(highPriority, lowPriority),
            new BehaviorActionRegistry(),
            blackboard: blackboard);

        tree.Start();

        Assert.Equal(BehaviorTreeStatus.Running, tree.Status);

        blackboard.Set("ready", true);

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
        Assert.Equal(["high"], calls);
    }

    [Fact]
    public void Multiframe_action_receives_update_and_cancel_requests()
    {
        using var scene = new Scene("behavior");
        var requests = new List<BehaviorActionRequest>();
        var tree = new BehaviorTreeInstance(
            new BehaviorActionNode(new DelegateBehaviorAction(context =>
            {
                requests.Add(context.Request);
                return context.Request == BehaviorActionRequest.Cancel
                    ? BehaviorActionStatus.Failure
                    : BehaviorActionStatus.Running;
            })),
            new BehaviorActionRegistry(),
            new BehaviorTreeContext(scene));

        tree.Start();
        tree.Update(TimeSpan.FromSeconds(0.1));
        tree.Cancel();

        Assert.Equal(BehaviorTreeStatus.Canceled, tree.Status);
        Assert.Contains(BehaviorActionRequest.Start, requests);
        Assert.Contains(BehaviorActionRequest.Update, requests);
        Assert.Contains(BehaviorActionRequest.Cancel, requests);
    }

    [Fact]
    public void Action_update_receives_driver_frame_time()
    {
        using var scene = new Scene("behavior");
        long? frame = null;
        TimeSpan? deltaTime = null;
        var tree = new BehaviorTreeInstance(
            new BehaviorActionNode(new DelegateBehaviorAction(context =>
            {
                if (context.Request == BehaviorActionRequest.Update)
                {
                    frame = context.Time.Frame;
                    deltaTime = context.DeltaTime;
                    return BehaviorActionStatus.Success;
                }

                return BehaviorActionStatus.Running;
            })),
            new BehaviorActionRegistry(),
            new BehaviorTreeContext(scene));
        var time = new GameFrameTime(7, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.2));

        tree.Start();
        tree.Update(in time);

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
        Assert.Equal(7, frame);
        Assert.Equal(TimeSpan.FromSeconds(0.1), deltaTime);
    }
}
