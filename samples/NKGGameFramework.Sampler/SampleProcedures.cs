using NKGGameFramework.Core;

namespace NKGGameFramework.Sampler;

internal sealed class BootProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        game.Log("boot enter");
    }

    protected override void OnUpdate(Fsm<IProcedureModule> fsm, double deltaTime, double realDeltaTime)
    {
        // Procedure 内部通过 ChangeProcedure 控制流程跳转。
        // 外部主循环不需要知道当前处于哪个游戏阶段。
        ChangeProcedure<LoadProcedure>(fsm);
    }
}

internal sealed class LoadProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        // 加载阶段通常负责配置、资源、场景和基础实体准备。
        game.Log("load enter");
        game.LoadGameConfig();
        game.CreateBattleScene();
    }

    protected override void OnUpdate(Fsm<IProcedureModule> fsm, double deltaTime, double realDeltaTime)
    {
        ChangeProcedure<GameplayProcedure>(fsm);
    }
}

internal sealed class GameplayProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        game.Log("gameplay enter");
    }

    protected override void OnUpdate(Fsm<IProcedureModule> fsm, double deltaTime, double realDeltaTime)
    {
        // 玩法阶段每帧推进 ECS 世界，再根据业务条件进入下一个 Procedure。
        game.Frame++;
        game.UpdateBattle(deltaTime, realDeltaTime);
        game.Log($"gameplay frame={game.Frame}");

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
        // 存档放在 OnEnter，表示进入该流程节点时只执行一次。
        game.Log("save enter");
        game.SaveSnapshot();
    }

    protected override void OnUpdate(Fsm<IProcedureModule> fsm, double deltaTime, double realDeltaTime)
    {
        ChangeProcedure<ExitProcedure>(fsm);
    }
}

internal sealed class ExitProcedure(SampleGame game) : ProcedureBase
{
    protected override void OnEnter(Fsm<IProcedureModule> fsm)
    {
        game.Log("exit enter");
        game.RequestExit();
    }
}
