namespace NKGGameFramework.SkillSystemSampler;

internal static class Program
{
    public static void Main()
    {
        // 独立入口只负责启动技能系统演示，具体玩法流程放在场景编排类中。
        SkillSystemSample.Run();
    }
}
