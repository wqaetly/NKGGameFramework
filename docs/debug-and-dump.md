# Debug and Dump Flow

本文记录 NKGGameFramework 当前 WebDebug 调试链路，以及后续一段时间内游戏状态 dump / recorder 的设计方向。目标是把 live inspector、自动刷新、组件编辑、帧级记录和离线 dump 拆清楚，避免回到“每次粗暴 Odin 序列化整个 World”的方案。

## Scope

Debug 能力分两层：

- WebDebug：在线检查器，用于查看当前运行态、暂停/单步、按需查看实体组件值、编辑组件字段。
- Debug Recorder / Dump：离线诊断数据，用于保留一段时间内的世界状态变化、事件、结构变化和必要的组件值。

两者共享框架层 introspection 能力，但数据粒度不同。WebDebug 以“当前状态 + 懒加载 detail”为主；Recorder 以“帧序列 + ring buffer + 可查询索引”为主。

## Current WebDebug Flow

### Startup

```mermaid
flowchart TD
    Runtime["RuntimeContext / World created"] --> Registry["GameDebugRuntimeRegistry auto-register"]
    HostStart["GameDebugHost / ASP.NET host starts"] --> Services["AddNkgGameDebugging"]
    Services --> Session["GameDebugSession"]
    Registry --> Session
    Session --> Endpoints["MapNkgGameDebugEndpoints"]
    Endpoints --> Web["Hosting.Web Debug Inspector"]
```

当前框架内置这些调试入口：

- `GameDebugRuntimeRegistry`：自动跟踪当前进程内创建的 `RuntimeContext` 和 `World`。
- `GameDebugSession`：显式注册或默认读取 registry 中的运行态对象。
- `GameDebugHost`：本地 ASP.NET Core debug host。
- `GameDebugHostAutoStart`：通过环境变量开启本地 debug host。
- `MapNkgGameDebugEndpoints`：暴露 `/_nkg/debug/*` HTTP API。

常用环境变量：

```powershell
$env:NKG_DEBUG_HOST = "1"
$env:NKG_DEBUG_HOST_URL = "http://127.0.0.1:5000"
$env:NKG_DEBUG_HOST_PREFIX = "/_nkg/debug"
$env:NKG_DEBUG_HOST_MUTATIONS = "1"
```

### HTTP API

当前 WebDebug 使用这些端点：

- `GET /_nkg/debug/health`
- `GET /_nkg/debug/snapshot`
- `GET /_nkg/debug/stream`
- `GET /_nkg/debug/control`
- `POST /_nkg/debug/control`
- `POST /_nkg/debug/mutations`

`/_nkg/debug/snapshot` 返回 `GameDebugSnapshotMessage`，包含 `frame`、`snapshot` 和 `control`。`/_nkg/debug/stream` 通过 SSE 推送同一种 message。两者共享相同的后端 capture 终点，前端也进入同一个 summary commit 逻辑。

`/_nkg/debug/snapshot` 和 `/_nkg/debug/stream` 都支持按需裁剪：

| Query | 用途 |
| --- | --- |
| `worldName` | 限定 World |
| `sceneName` | 限定 Scene |
| `entityId` | 限定单个 Entity |
| `entityOffset` / `entityLimit` | Entity 分页 |
| `includePayload` | 是否包含 Odin JSON payload |
| `includeStructured` | 是否包含结构化字段树 |

默认 WebDebug overview 请求必须使用：

```text
includePayload=false&includeStructured=false
```

这样首包只包含 world / scene / entity / component type / graph / skill / buff 摘要，不序列化所有组件值。

### Snapshot Provider

```mermaid
flowchart TD
    Web["WebDebug fetch snapshot"] --> Endpoint["/_nkg/debug/snapshot"]
    Endpoint --> Provider["GameDebugSnapshotProvider"]
    Provider --> Session["GameDebugSession"]
    Session --> Runtime["RuntimeContext"]
    Session --> World["World"]
    World --> Scene["Scene"]
    Scene --> Stores["ComponentStores"]
    Scene --> Entities["Entities"]
    Provider --> Serializer["IGameDebugComponentValueSerializer"]
    Serializer --> Odin["Odin payload / structured value"]
    Provider --> Json["GameDebugSnapshotMessage JSON"]
```

`GameDebugSnapshotProvider` 负责收集：

- runtime modules / procedure modules / procedures
- worlds / scenes
- systems
- component stores
- entities
- components
- skills / buffs

组件值有两种表现：

- `payload`：Odin JSON，适合保存和回写，但体积较大。
- `structured`：结构化字段树，适合 WebDebug UI 直接展示和编辑。

WebDebug 不应在 overview 阶段请求这两种值；只有选择具体实体时才请求 detail。

### WebDebug UI Flow

```mermaid
sequenceDiagram
    participant UI as WebDebug UI
    participant API as Debug HTTP API
    participant Host as GameDebugSnapshotProvider

    UI->>API: GET /snapshot?includePayload=false&includeStructured=false
    API->>Host: Capture overview
    Host-->>API: summary snapshot + control
    API-->>UI: GameDebugSnapshotMessage
    UI->>UI: commitSnapshotMessage

    UI->>UI: user selects entity
    UI->>API: GET /snapshot?worldName=...&sceneName=...&entityId=...&includePayload=true&includeStructured=true
    API->>Host: Capture one entity detail
    Host-->>API: entity detail snapshot + control
    API-->>UI: GameDebugSnapshotMessage
    UI->>UI: commitEntityDetail

    UI->>UI: user edits structured field
    UI->>API: POST /mutations
    API->>Host: deserialize and SetComponent
    Host-->>API: mutation result
    API-->>UI: success / failure
```

前端当前行为：

- 初始加载只拉轻量 overview。
- 选择 entity 后才懒加载该 entity 的 payload / structured detail。
- Manual Refresh 和 Frame Stream 都落到 `commitSnapshotMessage`，统一更新 overview snapshot 和 control。
- detail cache key 为 `worldName + sceneName + entityId`。
- mutation 成功后清空 detail cache，并重新拉 overview。
- component panel 在 detail 未加载完成前显示 loading。

### Control Flow

`/_nkg/debug/control` 用于暂停、继续和单步：

```mermaid
flowchart LR
    UI["Play / Pause / Step"] --> ControlPost["POST /_nkg/debug/control"]
    ControlPost --> Controller["GameDebugController"]
    Controller --> Gate["TryBeginRuntimeFrame"]
    Gate --> RuntimeUpdate["RuntimeContext.Update"]
```

控制命令：

- `play`
- `pause`
- `step`

`RuntimeContext.Update` 在帧开始前通过 `GameDebugController.Shared.TryBeginRuntimeFrame()` 进入调试门控。暂停时不推进帧；step 会消费 pending step count。

### Refresh Modes

WebDebug 当前只保留两种刷新模式：

- Manual：默认模式。用户点击 `Refresh` 时才请求 snapshot message。
- Frame Stream：工具栏打开 `Frame` 后，前端通过 `EventSource` 订阅 `/_nkg/debug/stream`，由宿主在 `RuntimeContext.Update` 帧末尾推送同一种 snapshot message。

Frame Stream 的规则：

- 设置写入 `localStorage`。
- stream 连接建立后，宿主先发送一帧 `initial` summary。
- `RuntimeContext.Update` 完成模块和事件队列更新后，通过 `GameDebugFramePublisher` 发布 frame event。
- Hosting 收到 frame event 后捕获轻量 snapshot message，并通过 SSE 推送给 WebDebug。
- stream summary 强制关闭 `payload` / `structured`，不在推送流里序列化所有组件值。
- 若当前选中 entity 的 detail 已加载，WebDebug 收到 summary 后再按需请求该 entity detail。
- snapshot message 携带 control 状态，Manual 和 Frame Stream 都不需要额外轮询 control。

```mermaid
flowchart TD
    Runtime["RuntimeContext.Update completed"] --> Publisher["GameDebugFramePublisher.Publish"]
    Publisher --> Stream["/_nkg/debug/stream"]
    Stream --> Summary["SSE snapshot summary"]
    Summary --> Web["WebDebug commitSnapshotMessage"]
    Web --> Detail{"selected detail cached?"}
    Detail -->|yes| EntityDetail["GET selected entity detail"]
    Detail -->|no| Done["wait next host frame"]
    EntityDetail --> Done
```

### Host Frame To WebDebug Update

当前 WebDebug 能看到宿主每帧后的框架数据变化，依赖的是“宿主帧后推送 summary”：

```mermaid
sequenceDiagram
    participant Host as Game Host
    participant Runtime as RuntimeContext.Update
    participant State as World / Scene / ComponentStore
    participant Publisher as GameDebugFramePublisher
    participant Stream as /_nkg/debug/stream
    participant Snapshot as /_nkg/debug/snapshot
    participant Provider as GameDebugSnapshotProvider
    participant Web as WebDebug Frame Stream

    Host->>Runtime: Update(frame N)
    Runtime->>State: modules/systems mutate ECS state
    State-->>Runtime: frame N state committed
    Runtime->>Publisher: Publish(RuntimeContext, frame N)
    Publisher->>Stream: frame event
    Stream->>Provider: Capture(includePayload=false, includeStructured=false)
    Provider->>State: read current debug views
    State-->>Provider: latest state after frame N
    Provider-->>Stream: GameDebugSnapshotMessage
    Stream-->>Web: SSE snapshot event
    Web->>Web: commitSnapshotMessage
    Web->>Snapshot: GET selected entity detail (optional)
    Snapshot->>Provider: Capture(entityId, includePayload=true, includeStructured=true)
    Provider-->>Snapshot: selected entity snapshot message after frame N
    Snapshot-->>Web: commitEntityDetail
```

`GameDebugFramePublisher` 的发布点只放在 `RuntimeContext.Update` 末尾，因为框架约定整个框架层更新入口是 `RuntimeContext`。`Scene.Update` 不直接发布 frame event，避免内部场景更新造成重复推送。

`/_nkg/debug/stream` 是 live UI 通道，不是最终 recorder。它使用 bounded connection buffer；WebDebug 慢时 live 显示可以丢旧 summary，但后续 `GameDebugRecorder` 才负责权威逐帧不丢。

当前测试覆盖这条链路：

- `GameDebugHostTests.Host_stream_pushes_summary_snapshot_after_framework_frame`
- `GameDebugHostTests.Host_snapshot_requests_observe_framework_changes_after_each_runtime_frame`
- stream 测试启动真实 `GameDebugHost`，连接 `/_nkg/debug/stream`，读取 initial event。
- 每次 `runtime.Update(frame)` 后，测试断言 SSE 收到宿主推送的 summary snapshot。
- snapshot request 测试保留，用于验证 snapshot API 本身在每帧后请求时能读到最新 summary/detail。

## Component Graph Rendering

WebDebug 的组件图使用 React Flow。为避免 frame stream 高频更新时整图重绘，前端现在做了这些稳定化：

- `ComponentNode` / `ComponentGroupNode` 使用 memoized node type。
- node data 不再携带整个 entity；保存时通过 `entityRef` 读取最新 entity。
- `useStableComponents` 按 component graph id 复用未变化的 component snapshot。
- `useStableGraphElements` 按 node / edge id 复用未变化的 React Flow node / edge 对象。
- structured value tree 会尽量复用未变化的子树引用。
- 结构化字段编辑改为 path-based update，`StructuredValueEditor` / `ListField` / `ScalarField` 都可按参数子树 memo。

目标是 frame stream 更新时只有变化的 component / field 更新 UI，而不是每次重建全部节点。

## Mutation Flow

组件编辑仍走通用组件链路，不为 Skill / Buff 写特殊命令：

```mermaid
sequenceDiagram
    participant UI as Component Editor
    participant API as /mutations
    participant Handler as GameDebugMutationHandler
    participant Scene as Scene
    participant Serializer as Odin Serializer

    UI->>API: GameDebugMutationRequest
    API->>Handler: Execute
    Handler->>Scene: resolve world/scene/entity/component type
    Handler->>Serializer: deserialize payload/structured
    Handler->>Scene: SetComponent(entity, type, value)
    Scene-->>Handler: component updated
    Handler-->>API: GameDebugMutationResult
    API-->>UI: success/failure
```

Mutation 默认应只在开发环境开启。生产、外网和多人环境必须关闭或接入额外鉴权。

## Dump / Recorder Direction

一段时间内的游戏状态 dump 不应设计成“后台线程直接 Odin 序列化整个 live World”。原因：

- live World 由主线程更新，后台遍历会遇到集合修改和引用图不稳定。
- 全量 World 对象图包含很多调试不需要的运行时对象。
- 每帧完整序列化会造成 CPU、GC 和 IO 压力。
- 新增模块不应要求手写 delta。

推荐方向是框架层 recorder：

```mermaid
flowchart TD
    Frame["Frame boundary"] --> Recorder["GameDebugRecorder"]
    EventBus["EventBus"] --> Recorder
    CommandBuffer["EcsCommandBuffer"] --> Recorder
    Store["ComponentStore"] --> Recorder
    Systems["System timing"] --> Recorder

    Recorder --> Ring["In-memory ring buffer"]
    Ring --> Mark{"mark / assertion / crash?"}
    Mark -->|yes| Freeze["freeze pre/post window"]
    Freeze --> Writer["background compression + file writer"]
    Writer --> Dump["chunked debug dump"]
```

Recorder 分层：

| Mode | 内容 | 成本 | 用途 |
| --- | --- | --- | --- |
| `Trace` | 帧、system timing、事件、命令、结构变化 | 低 | 长期开启 |
| `StateIndex` | store/chunk version、hash、数量、分布 | 低到中 | 定位哪一帧开始异常 |
| `StateDelta` | 框架自动 dirty chunk value block | 中 | 追踪一段时间内状态变化 |
| `Keyframe` | 周期性完整 component store 快照 | 中到高 | 支持任意帧重建 |
| `FullWindow` | debug 专用高保真窗口 | 高 | 疑难 bug 临时开启 |

`mark` 的语义不是“从这一帧才开始抓”，而是“冻结已经在滚动记录的窗口”。例如保留 `[-10s, +3s]`，用户点 mark、断言失败或异常触发时，把这一段窗口落盘。

## Recorder Capture Rules

框架层自动捕获，不要求业务模块手写 delta：

- `RuntimeContext`：frame begin/end、frame index、delta time、pause/step 状态。
- `SystemGroup` / `ISystem`：system timing、执行顺序、异常。
- `EventBus`：publish/fire/fire-now 事件元数据。
- `EcsCommandBuffer`：create/destroy entity、add/remove/set component 等命令。
- `Scene`：entity 生命周期、component 结构变化。
- `ComponentStore`：store version、chunk version、dirty chunk 标记。

组件值捕获规则：

- 默认不捕获所有值，只捕获 store / chunk 摘要。
- watchlist 命中的 component type / entity 可以捕获详细值。
- dirty chunk 模式下，只复制发生写入的 chunk。
- 如果无法精确追踪字段写入，只要发放 mutable ref 或执行 set，就把对应 chunk 标脏。
- 非 ECS 状态通过可选 `IDebugStateProvider` 接入，但不要求每个业务模块实现 delta。

## Dump File Shape

推荐文件格式是 chunked + compressed + indexed：

```text
NKGDUMP
  Header
  TypeTable
  StreamInfo
  FrameIndex
  EntityIndex
  ComponentTypeIndex
  Chunks[]
```

chunk 类型：

- `FrameHeader`
- `SystemSample`
- `EventRecord`
- `CommandRecord`
- `StructuralChange`
- `StoreSummary`
- `DirtyChunkValueBlock`
- `Keyframe`
- `Bookmark`
- `Exception`

写盘线程只处理不可变数据：

1. 主线程在帧边界复制稳定 DTO / binary block。
2. ring buffer 接收这些不可变 block。
3. 后台线程压缩和写盘。
4. 后台线程不直接遍历 live `World`。

## Replay / Query Model

离线查看时不需要一次性读完整 dump：

```mermaid
flowchart LR
    File["dump file"] --> Header["read header/type table"]
    Header --> Index["read frame/entity/type indexes"]
    Index --> Timeline["show timeline summary"]
    Timeline --> Detail["user selects frame/entity/type"]
    Detail --> Chunk["load compressed chunk"]
    Chunk --> View["render detail"]
```

某一帧状态重建：

1. 找到目标帧之前最近的 keyframe。
2. 加载 keyframe 中的 component store 状态。
3. 应用 keyframe 之后到目标帧之间的 structural changes 和 dirty chunks。
4. 渲染目标 frame 的 entity/component detail。

这和 WebDebug 的懒加载思路一致：先读摘要和索引，点开后再读具体值。

## Current Status

已落地：

- Debug Host / Runtime registry / Debug session。
- Snapshot API，支持 world/scene/entity 过滤、entity 分页、payload/structured 开关。
- Control API，支持 play / pause / step。
- Mutation API，支持通用 component value 编辑。
- WebDebug overview 懒加载，不在首包抓 payload / structured。
- 选中 entity 后再抓 detail。
- `GameDebugFramePublisher` 在 `RuntimeContext.Update` 末尾发布 frame event。
- `/_nkg/debug/stream` 通过 SSE 推送宿主帧后的轻量 summary snapshot。
- WebDebug `Frame` 模式通过 `EventSource` 订阅 host frame stream。
- Component graph 稳定化，避免 Frame Stream 更新时整图重绘。

待落地：

- `GameDebugRecorder` ring buffer。
- `Trace` / `StateIndex` / `StateDelta` / `Keyframe` / `FullWindow` 捕获模式。
- dump 文件格式、索引和压缩。
- WebDebug timeline / dump viewer。
- watchlist / bookmark / assertion-triggered dump。

## Design Rules

- WebDebug 默认不能高频全量 payload。
- Frame Stream 只推 summary；detail 必须按需或按变化刷新。
- Recorder 可以自动记录框架层 delta，但业务模块不应手写 delta。
- 后台线程只能处理稳定 copy，不能直接遍历 live World。
- 全局完整 dump 只作为手动、低频、明确 expensive 的诊断按钮。
- Dump 文件必须支持按 frame / entity / component type 查询。
- Debug 功能默认面向本地开发，不作为生产公网能力暴露。
