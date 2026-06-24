using NKGGameFramework.Core;

namespace NKGGameFramework.Sampler;

internal sealed class BootProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        game.Log("进入启动流程");
    }

    protected override void OnUpdate(Fsm<IProcedureModule> fsm, in GameFrameTime time)
    {
        // 流程内部通过切换方法控制流程跳转。
        // 外部主循环不需要知道当前处于哪个游戏阶段。
        ChangeProcedure<LoadProcedure>(fsm);
    }
}

internal sealed class LoadProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        // 加载阶段通常负责配置、资源、场景和基础实体准备。
        game.Log("进入加载流程");
        game.LoadGameConfig();
        game.CreateBattleScene();
    }

    protected override void OnUpdate(Fsm<IProcedureModule> fsm, in GameFrameTime time)
    {
        ChangeProcedure<GameplayProcedure>(fsm);
    }
}

internal sealed class GameplayProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        game.Log("进入玩法流程");
    }

    protected override void OnUpdate(Fsm<IProcedureModule> fsm, in GameFrameTime time)
    {
        // 玩法阶段每帧推进实体组件世界，再根据业务条件进入下一个流程。
        game.Frame++;
        game.UpdateBattle(in time);
        game.Log($"玩法帧={game.Frame}");

        if (game.Frame >= game.Config.MaxFrames)
        {
            ChangeProcedure<SaveProcedure>(fsm);
        }
    }
}

internal sealed class SaveProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        // 存档放在进入回调中，表示进入该流程节点时只执行一次。
        game.Log("进入存档流程");
        game.SaveSnapshot();
    }

    protected override void OnUpdate(Fsm<IProcedureModule> fsm, in GameFrameTime time)
    {
        ChangeProcedure<ExitProcedure>(fsm);
    }
}

internal sealed class ExitProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        game.Log("进入退出流程");
        game.RequestExit();
    }
}
