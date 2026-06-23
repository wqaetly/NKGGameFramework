# NKGGameFramework Architecture

NKGGameFramework 的核心原则是：底层不依赖任何游戏引擎，所有 Unity、Godot、Server 差异通过 Adapter 或 Hosting 层接入。

## Projects

```text
src/
  NKGGameFramework/
    Core/
    Ecs/
    Gameplay/
    Runtime/
    Async/
    Serialization/
    Hosting/Server/
  NKGGameFramework.Adapter.Unity/
  NKGGameFramework.Adapter.Godot/

externals/
  odin-serializer/

tests/
  NKGGameFramework.Tests/

samples/
  NKGGameFramework.Sampler/
```

## Dependency Direction

```text
OdinSerializerNetCore -> TeamSirenix standalone source, patched in wqaetly fork for .NET 10
  ^
  |
NKGGameFramework
  ^
  |
Adapter.Unity / Adapter.Godot

NKGGameFramework.Tests -> NKGGameFramework
```

Rules:

- `NKGGameFramework` 是唯一主包，包含所有引擎无关能力。
- `NKGGameFramework` 直接引用 Odin standalone Net10 项目，作为项目通用序列化底座。
- `NKGGameFramework` 直接引用 UniTask NuGet 包，作为项目通用 async/await awaitable 底座。
- `Adapter.Unity` / `Adapter.Godot` 引用主包，主包不反向引用 Adapter。
- Unity/Godot/YooAsset/HybridCLR/Luban 等引擎或生成管线依赖只能出现在 Adapter 的实际引擎实现包中，不能出现在主包。

## Core

Core 提供：

- `RuntimeContext`：可实例化模块上下文，避免全局静态污染。
- `Module` / `IUpdateModule`：显式注册、按优先级更新、反向关闭。
- `EventBus` / `EventModule`：强类型事件总线，支持立即派发、队列派发、重复订阅检查、异常策略和池化事件参数。
- `MemoryPool<T>`：严格 double-release 检测。
- `ObjectPool<T>`：容量、过期、优先级释放。
- `Fsm<T>` / `ProcedureModule`：流程状态机和游戏生命周期管理。
- `TimerService`：引擎无关 timer。

事件系统保持 Runtime 和 Scene 两层作用域。`Publish` / `FireNow` 会立即派发，适合 ECS 生命周期这类同步结构变化；`Fire` 会入队，并由 `RuntimeContext.Update` 或 `Scene.Update` 在当前帧末尾派发，适合业务事件解耦和避免重入。热路径事件可以继承 `GameEventArgs`，通过 `Rent` / `Return` 或 `FirePooled` 复用事件参数对象。

## ECS

ECS 提供轻量、单线程、引擎无关的数据组合模型：

- 运行边界：`World` / `Scene` 隔离，`EntityRef` 通过版本校验避免悬空实体引用。
- 数据模型：组件是 `struct IComponent`，查询支持 typed query 和 `ref` mutation。组件不做对象池化，也不应以 `IComponent` / `object` 存储；池化目标是 `EcsCommandBuffer`、command 和事件参数这类引用型临时对象。
- 结构变化：实体创建销毁、组件增删都视为 structural changes；查询迭代中禁止直接结构变化，必须记录到 `EcsCommandBuffer` 后播放。CommandBuffer 由 Scene 级对象池复用，`Playback` 或 `Dispose` 后归还。
- System 调度：`SystemGroup` 按 `Order` 排序，统一驱动 System 更新。
- System 生命周期：`OnCreate`、`OnStartRunning`、`Update`、`OnStopRunning`、`OnDestroy`。
- 组件变化回调：`IComponentAddedSystem<T>`、`IComponentUpdatedSystem<T>`、`IComponentRemovedSystem<T>`，用于在组件增删改时派生逻辑或补全组合。
- 生命周期事件：实体创建销毁、组件增删改会发布到 Scene 级 `EventBus`。

## Gameplay

Gameplay 是从 `NKGMobaBasedOnET` 的技能系统、Buff 系统，以及 UE 5.7 `GameplayTags` 运行时语义抽取出的引擎无关业务运行时。参考仓库中的 ET `Unit`、`Room`、行为树、Odin Inspector、Unity 资源和 FairyGUI 代码不进入主包；UE 的 Editor、ini 导入、重定向、网络压缩索引和反射宏也不进入主包。主包只保留可复用的状态机、数据定义、标签匹配、扩展注册和 ECS 驱动方式。

GameplayTag 运行时包含：

- `GameplayTag`：点分层级标签，例如 `State.Control.Silenced`。`State.Control.Silenced` 可匹配 `State.Control`，反向不成立。
- `GameplayTagContainer`：显式标签集合，并按 UE 语义维护父标签匹配能力；支持 `HasTag`、`HasTagExact`、`HasAny`、`HasAll`、`Filter`、批量追加/移除、匹配追加、显式集合相等和简单字符串输出等操作。
- `GameplayTagQuery` / `GameplayTagQueryExpression`：支持 Any/All/None 与 exact/non-exact tag query，也支持表达式组合。
- `GameplayTagRegistry`：可选注册表，用于业务或工具链校验已注册标签；记录 source、DevComment、restricted 标记、redirect 和隐式父节点；核心匹配不依赖全局单例。
- `GameplayTagConfigParser`：解析 UE `DefaultGameplayTags.ini` 常见条目，包括 `GameplayTagList`、`RestrictedGameplayTagList` 和 `GameplayTagRedirects`。
- `GameplayTagTableParser`：导入 UE GameplayTag DataTable 常见 CSV 形态，读取 `Tag`、`DevComment` 和 `bAllowNonRestrictedChildren` 列。
- `IGameplayTagAsset` / `EntityGameplayTagAsset`：对齐 UE `IGameplayTagAssetInterface` 的 owned tags 查询语义，提供 `HasMatchingGameplayTag`、`HasAllMatchingGameplayTags` 和 `HasAnyMatchingGameplayTags`。
- `GameplayTagComponent`：ECS 实体可挂基础标签；`GameplayTagUtility.GetOwnedTags` 会合并实体基础标签和激活 Buff 授予的标签。

Buff 运行时包含：

- `BuffDefinition`：Buff 数据块，包含唯一标识、来源技能、目标选择、正负面类型、伤害标签、可见性、同步标记、持续时间、叠层数量、刷新策略、授予标签、Required/Blocked 标签 gate 和 source/target `GameplayTagQuery` gate。
- `BuffInstance` / `BuffCollectionComponent`：实体上的运行时 Buff 列表，记录来源、目标、等级、层数、剩余时间和状态。
- `BuffManager`：添加、刷新、查询和标记移除 Buff。默认按 `BuffDefinition.Id` 合并层数；`UniquePerSource` 可按来源实体隔离实例，避免参考仓库里同 ID 不同来源互相覆盖的问题。
- `BuffUpdateSystem`：ECS 系统，按帧推进 `Waiting -> Running/Forever -> Finished` 生命周期，触发 `IBuffEffect` 的 apply、refresh、update、remove 回调。
- `BuffEffectRegistry`：注册具体 Buff 行为。属性修改、伤害、治疗、动画、特效、材质替换等都可以作为 effect 注册；涉及引擎对象的 effect 应放在 Adapter 或业务项目。

Skill 运行时包含：

- `SkillDefinition`：技能基础信息、等级 CD、消耗、释放模式、标签、Required/Blocked 标签 gate 和 caster/target `GameplayTagQuery` gate。
- `SkillBookComponent` / `SkillSlot`：实体上的技能书、等级和 CD 状态。
- `SkillManager`：学习技能、释放校验、标签 gate、消耗策略、执行技能效果和发布释放事件。
- `SkillCooldownSystem`：按帧推进技能 CD。
- `SkillEffectRegistry`：注册技能效果。默认 `apply_buff` 效果把技能释放映射到 `BuffManager.Apply`。

示例：

```csharp
var scene = new Scene("battle");
scene.Systems.Add(new MovementSystem());

var unit = scene.CreateEntity()
    .Add(new Position(0, 0))
    .Add(new Velocity(2, 3));

scene.Update(0.5, 0.5);
```

## Runtime

Runtime 只定义引擎无关能力：

- Asset
- Scene
- Audio
- UI
- Config
- Localization
- Presentation binding
- MVVM binding

Unity 或 Godot 的具体资源、场景、音频、UI 接入应放到 adapter 实现包中。

Runtime 的异步接口统一返回 `UniTask` / `UniTask<T>`，使业务侧和 Adapter 侧使用一致的 await/async 工具。

MVVM 基础设施保持 UI 框架无关：

- `ViewModelBase`：基于 `INotifyPropertyChanged` 的 ViewModel 基类。
- `BindableValue<T>` / `IBindingTarget<T>`：Adapter 可实现的绑定目标。
- `BindingSet`：管理 one-time、one-way、two-way binding 生命周期。

Unity/FairyGUI、UGUI、Godot Control 或测试视图只需要把具体控件包装成 `IBindingTarget<T>`，主包不持有任何 UI 控件类型。

## Async

Async 内置 Cysharp UniTask 作为项目通用 awaitable。框架提供 `GameAsync` 入口封装常用的 completed、result、exception、canceled、WhenAll 和 WhenAny 创建/组合操作，避免业务层散落不同 task 类型。

## Serialization

Serialization 提供三层接口：

- `IGameSerializer`：字符串 payload，适合配置、存档文本管线或简单集成。
- `IBinaryGameSerializer`：二进制 payload，适合存档、网络快照、缓存和热路径。
- `IJsonGameSerializer`：Odin JSON 文本 payload，适合调试、配置导出和需要人工查看的存档。

默认通用实现是 `OdinGameSerializer`。它使用 Odin 的 `SerializationPolicies.Everything`，可以在不写 attribute、不生成代码的前提下序列化私有字段、多态对象图和集合。默认 `IGameSerializer` 兼容层使用 Base64 包装二进制 payload，避免破坏现有接口；构造为 `DataFormat.JSON` 时，字符串接口直接读写 Odin JSON 文本。
