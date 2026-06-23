# NKGMobaBasedOnET Reference Analysis

本次参考 `E:\Study\wqaetly\NKGMobaBasedOnET` 时，只抽取能进入引擎无关主包的结构，不迁移 ET、Unity、FairyGUI、Odin Inspector、行为树运行时或具体 MOBA 业务逻辑。

## Skill

参考点：

- `SkillDesNodeData`：技能名、描述、资源、消耗、CD、类型、释放模式。
- `SkillCanvasManagerComponent`：实体拥有技能集合和技能等级。
- 技能通过行为树/节点触发 Buff。

框架落点：

- `SkillDefinition` 保存技能基础数据、等级 CD、消耗和效果列表。
- `SkillBookComponent` / `SkillSlot` 保存实体技能书、等级和 CD。
- `SkillManager` 处理学习、释放校验、消耗策略和效果执行。
- `SkillEffectRegistry` 提供扩展点，默认 `apply_buff` 负责连接 Buff 系统。

## Buff

参考点：

- `BuffDataBase`：Buff 数据、目标、基础类型、可见性、同步标记、层数、持续时间、基础数值和加成。
- `ABuffSystemBase`：Waiting、Running、Finished、Forever 状态机。
- `BuffTimerAndOverlayHelper`：已有 Buff 刷新时间和叠层，新 Buff 加入运行时列表。
- `BuffManagerComponent`：实体 Buff 列表、查询和固定帧更新。

框架落点：

- `BuffDefinition` / `BuffInstance` 分离配置和运行时。
- `BuffCollectionComponent` 将 Buff 挂到 ECS 实体上。
- `BuffManager` 负责添加、刷新、查询和标记移除。
- `BuffUpdateSystem` 负责推进状态机和触发 effect。
- `BuffEffectRegistry` 让业务或 Adapter 注册具体效果。

额外修正：

- 参考仓库按 `BuffId` 查询时注释里已经指出“同 ID 不同来源”会有歧义。框架新增 `UniquePerSource`，需要时按来源实体隔离叠层实例。

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
