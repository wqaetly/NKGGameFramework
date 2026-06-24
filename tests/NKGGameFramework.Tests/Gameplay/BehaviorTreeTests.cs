using NKGGameFramework.Core;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.Tests.Gameplay;

public sealed class BehaviorTreeTests
{
    [Fact]
    public void Long_immediate_sequence_is_processed_without_recursive_stack_growth()
    {
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
            new BehaviorActionRegistry());

        tree.Start();

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
        Assert.Equal(20_000, calls);
    }

    [Fact]
    public void Wait_node_completes_after_elapsed_update_time()
    {
        var tree = new BehaviorTreeInstance(
            new BehaviorWaitNode(TimeSpan.FromSeconds(1)),
            new BehaviorActionRegistry());

        tree.Start();
        tree.Update(TimeSpan.FromSeconds(0.25));

        Assert.Equal(BehaviorTreeStatus.Running, tree.Status);

        tree.Update(TimeSpan.FromSeconds(0.75));

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
    }

    [Fact]
    public void Blackboard_condition_can_abort_self_when_value_changes()
    {
        var blackboard = new BehaviorBlackboard();
        blackboard.Set("can_continue", true);
        var tree = new BehaviorTreeInstance(
            new BehaviorBlackboardConditionNode(
                "can_continue",
                BehaviorConditionOperator.Equal,
                true,
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
    public void Blackboard_condition_can_restart_higher_priority_selector_branch()
    {
        var calls = new List<string>();
        var blackboard = new BehaviorBlackboard();
        blackboard.Set("ready", false);
        var highPriority = new BehaviorBlackboardConditionNode(
            "ready",
            BehaviorConditionOperator.Equal,
            true,
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
        var requests = new List<BehaviorActionRequest>();
        var tree = new BehaviorTreeInstance(
            new BehaviorActionNode(new DelegateBehaviorAction(context =>
            {
                requests.Add(context.Request);
                return context.Request == BehaviorActionRequest.Cancel
                    ? BehaviorActionStatus.Failure
                    : BehaviorActionStatus.Running;
            })),
            new BehaviorActionRegistry());

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
            new BehaviorActionRegistry());
        var time = new GameFrameTime(7, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.2));

        tree.Start();
        tree.Update(in time);

        Assert.Equal(BehaviorTreeStatus.Succeeded, tree.Status);
        Assert.Equal(7, frame);
        Assert.Equal(TimeSpan.FromSeconds(0.1), deltaTime);
    }
}
