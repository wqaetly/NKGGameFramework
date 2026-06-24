using NKGGameFramework.Core;
using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;

namespace NKGGameFramework.SkillSystemSampler;

internal static class SkillSystemSample
{
    public static SkillSystemSampleGame Start()
    {
        SampleLog.Write("技能系统示例开始");
        return new SkillSystemSampleGame();
    }
}

internal sealed class SkillSystemSampleGame : IDisposable
{
    private static readonly double[] CastTimes = [0.0, 1.2, 2.4];

    private readonly RuntimeContext _runtime = new();
    private readonly World _world = new("skill-system-sampler");
    private readonly Scene _scene;
    private readonly BehaviorActionRegistry _skillActions;
    private readonly Entity _caster;
    private readonly Entity _enemy;
    private double _elapsed;
    private int _nextCast;
    private int _round = 1;
    private bool _disposed;

    public SkillSystemSampleGame()
    {
        _scene = _world.CreateScene("skill-sampler");
        var burning = SampleDefinitions.CreateBurningBuff();
        _skillActions = SampleRegistries.CreateSkillBehaviorActions();
        var buffActions = SampleRegistries.CreateBuffBehaviorActions();
        var buffEffects = SampleRegistries.CreateBuffEffects();

        // 施法者保存连段状态；目标保存生命和火焰抗性，用来展示真实伤害绕过抗性。
        _caster = _scene.CreateEntity()
            .Add(new Health(100))
            .Add(new FireballComboComponent(nextProjectileCount: 1, maxProjectileCount: SampleConstants.MaxProjectiles));

        _enemy = _scene.CreateEntity()
            .Add(new Health(160))
            .Add(new FireResistance(fireDamageMultiplier: 0.5));

        // 系统顺序体现运行时职责：先推进冷却，再推进技能树，最后推进增益生命周期和增益树。
        _scene.Systems.Add(new SkillCooldownSystem(order: 0));
        _scene.Systems.Add(new BehaviorTreeUpdateSystem(order: 10));
        _scene.Systems.Add(new BuffUpdateSystem(buffEffects, buffActions, order: 20));

        SkillManager.Learn(_caster, SampleDefinitions.CreateTriFireballSkill(burning));
        _runtime.RegisterModule(new SkillSystemSampleModule(this));

        SampleLog.Write("法师学习三段式火球；敌人初始生命=160，火焰伤害承受倍率=50%");
    }

    public void Update(in GameFrameTime time)
    {
        _runtime.Update(in time);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        LogRoundSummary("结束时");
        _world.Dispose();
        _runtime.Dispose();
        _disposed = true;
        SampleLog.Write("技能系统示例结束");
    }

    private void UpdateFrame(in GameFrameTime time)
    {
        CastDueSkills();

        // RuntimeContext.Update 是唯一外部驱动入口；这里的模块负责把 Runtime 帧传递给 World。
        _world.Update(in time);
        _elapsed += time.DeltaSeconds;

        if (_elapsed >= SampleConstants.DemoLoopSeconds)
        {
            ResetRound();
        }
    }

    private void CastDueSkills()
    {
        // 三次释放分别展示 1/2/3 发火球，间隔留给上一棵技能树完成。
        while (_nextCast < CastTimes.Length && _elapsed + 0.0001 >= CastTimes[_nextCast])
        {
            var castNumber = _nextCast + 1;
            var result = SkillManager.TryCast(
                _scene,
                _caster,
                SampleConstants.SkillId,
                _enemy,
                behaviorActions: _skillActions);
            SampleLog.Write(result.Succeeded
                ? $"第 {_round} 轮第 {castNumber} 次施法成功：时间={_elapsed:0.0}秒"
                : $"第 {_round} 轮第 {castNumber} 次施法失败：时间={_elapsed:0.0}秒，原因={SampleCombat.FormatSkillFailure(result.FailureReason)}");
            _nextCast++;
        }
    }

    private void ResetRound()
    {
        LogRoundSummary($"第 {_round} 轮");
        _round++;
        _elapsed = 0;
        _nextCast = 0;

        BuffManager.Remove(_enemy, "burning");
        _enemy.Add(new Health(160));
        _caster.Add(new FireballComboComponent(nextProjectileCount: 1, maxProjectileCount: SampleConstants.MaxProjectiles));
        SampleLog.Write($"第 {_round} 轮开始：敌人生命重置为 160，火球连段重置。");
    }

    private void LogRoundSummary(string label)
    {
        var finalHealth = _enemy.Get<Health>().Value;
        var finalBurn = BuffManager.TryGet(_enemy, "burning", out var burn)
            ? $"{burn.Stacks}层，状态={SampleCombat.FormatBuffState(burn.State)}，剩余={SampleCombat.FormatSeconds(burn.RemainingDuration)}"
            : "无";
        SampleLog.Write($"{label}敌人生命={finalHealth}，灼烧={finalBurn}");
    }

    private sealed class SkillSystemSampleModule(SkillSystemSampleGame sample) : Module, IUpdateModule
    {
        public void Update(in GameFrameTime time)
        {
            sample.UpdateFrame(in time);
        }
    }
}
