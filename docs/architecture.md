# NKGGameFramework Architecture

NKGGameFramework 的核心原则是：底层不依赖任何游戏引擎，所有 Unity、Godot、Server 差异通过 Adapter 或 Hosting 层接入。

## Projects

```text
src/
  NKGGameFramework/
    Core/
    Ecs/
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

Unity 或 Godot 的具体资源、场景、音频、UI 接入应放到 adapter 实现包中。

Runtime 的异步接口统一返回 `UniTask` / `UniTask<T>`，使业务侧和 Adapter 侧使用一致的 await/async 工具。

## Async

Async 内置 Cysharp UniTask 作为项目通用 awaitable。框架提供 `GameAsync` 入口封装常用的 completed、result、exception、canceled、WhenAll 和 WhenAny 创建/组合操作，避免业务层散落不同 task 类型。

## Serialization

Serialization 提供三层接口：

- `IGameSerializer`：字符串 payload，适合配置、存档文本管线或简单集成。
- `IBinaryGameSerializer`：二进制 payload，适合存档、网络快照、缓存和热路径。
- `IJsonGameSerializer`：Odin JSON 文本 payload，适合调试、配置导出和需要人工查看的存档。

默认通用实现是 `OdinGameSerializer`。它使用 Odin 的 `SerializationPolicies.Everything`，可以在不写 attribute、不生成代码的前提下序列化私有字段、多态对象图和集合。默认 `IGameSerializer` 兼容层使用 Base64 包装二进制 payload，避免破坏现有接口；构造为 `DataFormat.JSON` 时，字符串接口直接读写 Odin JSON 文本。
