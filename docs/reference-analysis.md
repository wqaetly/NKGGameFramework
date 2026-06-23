# NKGMobaBasedOnET Reference Analysis

本次参考 `E:\Study\wqaetly\NKGMobaBasedOnET` 时，只抽取能进入引擎无关主包的结构，不迁移 ET、Unity、FairyGUI、Odin Inspector、NodeEditor 工具链或具体 MOBA 业务逻辑。NPBehave 行为树只迁移运行时语义，并针对调用栈过深问题做队列化优化。

## Skill

参考点：

- `SkillDesNodeData`：技能名、描述、资源、消耗、CD、类型、释放模式。
- `SkillCanvasManagerComponent`：实体拥有技能集合和技能等级。
- 技能通过行为树/节点触发 Buff。

框架落点：

- `SkillDefinition` 保存技能基础数据、等级 CD、消耗和效果列表。
- `SkillDefinition.ExecutionTree` 可选接入行为树，覆盖等待、播放动画/特效、触发 Buff 等流程式技能。
- `SkillBookComponent` / `SkillSlot` 保存实体技能书、等级和 CD。
- `SkillManager` 处理学习、释放校验、消耗策略、旧式同步效果执行和行为树启动。
- `SkillEffectRegistry` 提供扩展点，默认 `apply_buff` 负责连接 Buff 系统。

## Buff

参考点：

- `BuffDataBase`：Buff 数据、目标、基础类型、可见性、同步标记、层数、持续时间、基础数值和加成。
- `ABuffSystemBase`：Waiting、Running、Finished、Forever 状态机。
- `BuffTimerAndOverlayHelper`：已有 Buff 刷新时间和叠层，新 Buff 加入运行时列表。
- `BuffManagerComponent`：实体 Buff 列表、查询和固定帧更新。

框架落点：

- `BuffDefinition` / `BuffInstance` 分离配置和运行时。
- `BuffDefinition.ExecutionTree` 可选接入行为树，适合把 Buff 生命周期内的触发、延迟、周期性表现或业务动作数据化。
- `BuffCollectionComponent` 将 Buff 挂到 ECS 实体上。
- `BuffManager` 负责添加、刷新、查询和标记移除。
- `BuffUpdateSystem` 负责推进状态机、触发 effect，并启动/更新/取消 Buff 自身的行为树实例。
- `BuffEffectRegistry` 让业务或 Adapter 注册具体效果。

额外修正：

- 参考仓库按 `BuffId` 查询时注释里已经指出“同 ID 不同来源”会有歧义。框架新增 `UniquePerSource`，需要时按来源实体隔离叠层实例。

## NPBehave / BehaviorTree

参考点：

- `Root` 在子节点结束后下一帧重启，用于避免根节点无限递归。
- `Action` 支持单帧、多帧、取消和 `SUCCESS/FAILED/BLOCKED/PROGRESS`。
- `Wait` 使用锁步帧定时，`Blackboard` 通过 observer 延迟通知条件节点。
- `ObservingDecorator` 提供 Self、LowerPriority、Both、ImmediateRestart 等中断模式。
- 技能编辑器节点把播放动画、播放特效、等待、添加 Buff 等业务动作映射成 NP action。

框架落点：

- 不复制 ET `Entity`、`Unit`、`LSF_Component`、Animancer、Unity `GameObject`、NodeEditor 和 Odin Inspector 依赖。
- `BehaviorTreeInstance` 使用 execution request 队列泵推进节点，长同步 `Sequence` 不再递归调用父子节点，从根上规避调用堆栈过深。
- `BehaviorBlackboard` 借鉴 UE Blackboard observer；黑板变化进入行为树队列后触发条件节点重评估。
- `BehaviorTreeUpdateSystem` 借鉴 UE `BehaviorTreeComponent` 的 request/update 分离，先退出 ECS query 再执行 action，避免 action 期间结构变化破坏查询。
- 具体动作通过 `BehaviorActionRegistry` 注册；主包仅内置 `apply_buff` / `apply_skill_effects` 这类引擎无关桥接。

## MVVM

参考仓库 UI 更偏 FairyGUI/事件驱动，没有一个可直接迁入主包的纯 MVVM 模块。框架侧补的是最小引擎无关绑定底座：

- `ViewModelBase`：通用属性变更通知。
- `BindableValue<T>` / `IBindingTarget<T>`：UI adapter 可实现的绑定端点。
- `BindingSet`：管理 one-time、one-way、two-way binding 订阅生命周期。

具体控件、窗口栈、资源加载和代码生成仍然属于 Adapter 或业务层。

## UE GameplayTags

参考位置：

- `D:\UE_5.7\Engine\Source\Runtime\GameplayTags\Classes\GameplayTagContainer.h`
- `D:\UE_5.7\Engine\Source\Runtime\GameplayTags\Private\GameplayTagContainer.cpp`
- `D:\UE_5.7\Engine\Source\Runtime\GameplayTags\Classes\GameplayTagsManager.h`

迁移到主包的运行时语义：

- 点分层级标签，例如 `A.B.C`。
- 子标签匹配父标签：`A.B.C` matches `A.B`，但 `A.B` 不 matches `A.B.C`。
- Container 维护显式标签和父标签匹配能力。
- `HasTag` / `HasTagExact` / `HasAny` / `HasAll` / `Filter`。
- `AppendTags` / `AppendMatchingTags` / `RemoveTag` / `RemoveTags` / `RemoveTagByExplicitName`、显式集合相等和 `ToStringSimple`。
- `GameplayTagQuery` 支持 Any/All/None 和 exact/non-exact query，以及表达式组合。
- `GameplayTagRegistry` 支持 source、DevComment、restricted 标记、隐式父节点、redirect、children/direct children 查询。
- `GameplayTagConfigParser` 支持解析 UE 常见配置条目：`GameplayTagList`、`RestrictedGameplayTagList`、`GameplayTagRedirects`。
- `GameplayTagTableParser` 支持导入 GameplayTag DataTable 常见 CSV 形态。
- `IGameplayTagAsset` / `EntityGameplayTagAsset` 对齐 UE `IGameplayTagAssetInterface` 的 owned tags 查询语义。
- Skill/Buff 使用 Required/Blocked tags 和 `GameplayTagQuery` 做释放和添加 gate。

暂未迁移的 UE 工具链能力：

- Editor 面板和 Blueprint 节点。
- UE `UDataTable` 对象系统、完整 config layering、redirect 冲突诊断 UI。
- 网络压缩索引、replication 统计、token stream 序列化格式。
- UObject、反射宏和配置热重载。

## UE BehaviorTree

参考位置：

- `D:\UE_5.7\Engine\Source\Runtime\AIModule\Classes\BehaviorTree\BehaviorTreeComponent.h`
- `D:\UE_5.7\Engine\Source\Runtime\AIModule\Private\BehaviorTree\BehaviorTreeComponent.cpp`
- `D:\UE_5.7\Engine\Source\Runtime\AIModule\Private\BehaviorTree\BlackboardComponent.cpp`
- `D:\UE_5.7\Engine\Source\Runtime\AIModule\Private\BehaviorTree\Decorators\BTDecorator_Blackboard.cpp`

迁移到主包的运行时语义：

- 黑板 key observer 驱动条件重评估。
- decorator 条件变化不直接执行整棵树，而是发起 execution request。
- branch abort 区分 Self、LowerPriority、Both 和 restart。
- 任务完成、黑板变化、timer 到期都统一进入行为树调度队列。

暂未迁移的 UE 能力：

- UObject、反射、Blueprint、AIController、GameplayTask、EQS、调试器和编辑器资产。
- UE 内部搜索数据结构、实例栈、node memory 二进制布局和可视化日志。
