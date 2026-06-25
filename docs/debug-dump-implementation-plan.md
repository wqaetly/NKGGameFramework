# Debug Dump Implementation Plan

本文是 [`debug-and-dump.md`](./debug-and-dump.md) 的落地路线图。目标很直接：先把 dump 的大小画像看清，再把停止录制时的 keyframe + delta 写盘做出来，最后补齐回放和回归验证。

## Goals

- 先做 dump 分析报告，定位最占空间的类和字段。
- 再做停止录制时的压缩写盘，保留当前录制链路的通用性。
- 最后补齐回放重建和正确性验证。
- 不引入 temp file、frame reference table 或业务特化 recorder。

## Roadmap

| Phase | Deliverable | Depends On | Verify |
| --- | --- | --- | --- |
| 1 | `DumpAnalysisReport` schema + parser | Existing dump document shape | Can rank classes, fields, `payload`, and `structured` size |
| 2 | Stop-time compaction writer | Phase 1 baseline report | Stop saves a smaller `.nkgdump` and keeps playback working |
| 3 | Replay reconstruction | Phase 2 compact file | Can open existing dumps and rebuild target frames |
| 4 | Size tuning | Phases 1-3 | Can compare before/after and adjust keyframe cadence |

## Phase 1: Report Tool

1. Define a stable report schema.
2. Parse dump files without touching the write path.
3. Aggregate size by type, field, component, entity, and scene.
4. Separate `payload` and `structured` size accounting.
5. Export machine-readable JSON and a human-readable table.

建议优先把 report 做到可用，再开始压缩写盘。这样后面调 keyframe 间隔、delta 粒度和字段裁剪时，判断依据会更实。

## Phase 2: Stop-Time Compaction

1. 录制期间仍然只收完整帧到内存。
2. 点击停止录制时，先冻结内存里的帧列表。
3. 按固定间隔切关键帧，初版先用 60 帧一组。
4. 以最近的关键帧为基准计算每帧差量。
5. 把关键帧和差量一起序列化成 compact dump。
6. 冻结后的压缩和写盘可以放后台线程做。

这一步的边界要守住：

- 不在 gameplay 运行时做差量整合。
- 不把临时文件引进录制链路。
- 不为 `SkillDefinition`、`BehaviorTreeDefinition` 写特殊 recorder 逻辑。

## Phase 3: Replay and Compatibility

1. 按 header 和索引打开 compact dump。
2. 从目标帧前最近的关键帧开始重建。
3. 逐帧应用差量，恢复目标帧。
4. 让现有 playback 接口继续可用。
5. 增加帧一致性和文件可读性回归测试。

## Acceptance Criteria

- 报告工具能明确指出最重的类型和字段。
- 报告工具能分别展示 `payload` 和 `structured` 的占比。
- 停止录制后的文件比当前全量窗口更小。
- 回放仍然能重建出正确帧。
- 录制器没有出现业务特化分支。
- 没有引入 temp file 或 frame reference table。

## Risks

- keyframe 间隔太短会增大文件，太长会增大回放成本。
- delta 粒度太细会增加计算量，太粗会降低压缩收益。
- 报告工具如果只给总量，不给字段级归因，就不够指导优化。

## Suggested Order

1. 先落 report schema 和 parser。
2. 再落 stop-time compaction writer。
3. 然后接 replay reconstruction。
4. 最后做 benchmark 和 keyframe cadence tuning。
