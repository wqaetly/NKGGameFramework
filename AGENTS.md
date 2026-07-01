# Repository Guidelines
git commit message使用中文，并给出详细更改概括

每轮对话必须按顺序执行：

用户消息 → 1) search_memory → 2) 回答 → 3) add_message
回答前调用 search_memory，用 filter / tags / conversation_id 精确限定本项目范围
仅使用与当前问题真正相关的记忆；无关或噪声忽略
回答后调用 add_message，把本轮 user+assistant 消息和 info / tags 元数据写入
无论用户说什么都要执行第 3 步，否则后续 search_memory 拿不到更细的用户信息。

推荐默认参数
search_memory（项目级共享）
{
  "query": "<当前用户问题摘要>",
  "conversation_id": "<stableConvId>",
  "memory_limit_number": 3,
  "include_preference": false,
  "include_tool_memory": false,
  "include_skill": false,
  "filter": {
    "and": [
      { "app_id": "<项目名>" }
    ]
  }
}
关键点：
项目名：根据当前项目名传入真实string
memory_limit_number: 3：默认 9 会导致召回噪声过多，3 条是项目实测的甜点值
include_preference / include_tool_memory / include_skill 全关：减少不必要的分支查询
conversation_id：建议 md5(user_id + 会话第一条用户消息)，单次会话保持稳定（云端会用它做相关性加权）
add_message（项目级共享）
{
  "conversation_id": "<stableConvId>",
  "app_id": "<项目名>",
  "async_mode": true,
  "messages": [
    { "role": "user", "content": "<用户本轮问题>" },
    { "role": "assistant", "content": "<助手最终回答>" }
  ],
  "tags": ["<稳定关键词1>", "<稳定关键词2>"],
  "info": {
    "agent_id": "<可选，区分多 Agent 实例>",
    "module": "<如 FrontEnd / Backend / DevOps>",
    "business_type": "<如 web_app / script / build_tool>",
    "biz_id": "<业务实体 ID，如类名/模块名>",
    "topic": "<一句话主题>",
    "scene": "<coding / debug / daily_chat / qa>",
    "lang": "zh"
  }
}
关键点：

async_mode: true：默认即异步，无需阻塞对话；同步模式建议仅在批处理脚本中使用
info 与 tags 的所有键都可作为 search_memory.filter 的扁平字段，不要写成 info.xxx
项目级隔离推荐用 app_id 作 filter；多团队/多用户场景再叠加 agent_id
## Project Structure & Module Organization

NKGGameFramework is a .NET 10 game framework with engine-independent core code in `src/NKGGameFramework`. Diagnostics and local debug hosting live in `src/NKGGameFramework.Diagnostics` and `src/NKGGameFramework.Hosting`. Engine boundaries are split into `src/NKGGameFramework.Adapter.Unity` and `src/NKGGameFramework.Adapter.Godot`. The React debug inspector is in `src/NKGGameFramework.Hosting.Web`. Tests are centralized under `tests/NKGGameFramework.Tests`, samples under `samples`, and external dependencies such as Odin Serializer under `externals`.

## Build, Test, and Development Commands

- `dotnet build NKGGameFramework.sln`: build all .NET projects.
- `dotnet test NKGGameFramework.sln`: run the full xUnit test suite.
- `npm --prefix src/NKGGameFramework.Hosting.Web test`: run Hosting Web Node tests.
- `npm --prefix src/NKGGameFramework.Hosting.Web run build`: type-check and build the React inspector.

Run the relevant .NET and web validators before handing off changes that touch those areas.

## Coding Style & Naming Conventions

Use C# conventions already present in the repository: four-space indentation, PascalCase for public types and members, camelCase for locals and parameters, and `_camelCase` for private fields. Keep the core package engine-agnostic; do not introduce Unity, Godot, hosting, or web dependencies into `src/NKGGameFramework`. TypeScript uses existing React functional component patterns and strict typing from `types.ts`.

## Testing Guidelines

Add or update xUnit tests in `tests/NKGGameFramework.Tests` for runtime, ECS, diagnostics, hosting, and adapter behavior. Name tests as behavior statements, for example `Dump_recorder_keeps_all_recorded_frames`. For web utilities, add `*.test.ts` files under `src/NKGGameFramework.Hosting.Web/src` and run the Node test command.

## Commit & Pull Request Guidelines

Recent commits use concise Chinese imperative summaries, such as `补齐 Hosting Web 模块模型测试` or `修复 Godot 调试录制卡顿与启动流程`. Keep commits focused and mention the affected module. Pull requests should include a short problem/solution summary, tests run, linked issues when applicable, and screenshots or recordings for UI/debug-inspector changes.

## Agent-Specific Instructions

Preserve untracked user work. Prefer targeted edits, match existing patterns, and avoid adding dependencies unless already used or explicitly approved.
