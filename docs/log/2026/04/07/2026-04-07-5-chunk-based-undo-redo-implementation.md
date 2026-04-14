# Chunk 化 Undo/Redo 实施
**Date**: 2026-04-07
**Session**: 5
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 将 Terrain Editor 的 Undo/Redo 从“区域快照”重构为“chunk-based stroke transaction”，修复 Paint Mode 刷纹理卡顿。

**Secondary Objectives:**
- 对齐 `godot_heightmap_plugin` 的编辑历史思路（笔触期间标记，提交时快照）。
- 保持 Height/Paint 两条编辑链路一致。
- 在关键复杂路径补充必要注释，方便后续维护。

**Success Criteria:**
- Paint/Height 笔触不再在开始阶段执行整图快照。
- 无改动笔触不进入历史栈。
- 工程可正常构建通过。

---

## Context & Background

**Previous Work:**
- [2026-04-07-4-undo-redo-design](./2026-04-07-4-undo-redo-design.md)
- 已提交代码变更：`cd8d5af`

**Current State:**
- 旧实现在 `BeginCommand` 阶段抓取 before-state，且在区域未收敛时会退化为整图复制，导致 Paint 交互卡顿。

**Why Now:**
- 用户反馈“最近提交让 Paint Mode 刷纹理很卡”，属于直接影响编辑体验的高优先级问题。

---

## What We Did

### 1. 引入 Chunk 跟踪器并重构命令基类
**Files Changed:** `Terrain.Editor/Services/Commands/StrokeChunkTracker.cs`, `Terrain.Editor/Services/Commands/TerrainEditCommand.cs`

**Implementation:**
- 新增 `StrokeChunkTracker`，统一执行：
  - 刷子圆形影响范围 -> chunk 网格映射（默认 64x64）
  - chunk 去重
  - 稳定顺序输出（便于调试和可重复回放）
- `TerrainEditCommand` 改为：
  - `MarkAffectedArea(...)` 期间只标记并对“首次触达 chunk”抓取 before-state
  - `PrepareForCommit()` 在笔触结束时统一抓 after-state 并过滤 unchanged chunk

**Rationale:**
- 避免笔触开始时的大块快照，改为仅按真正触达的 chunk 捕获状态。

---

### 2. 历史管理器切换为“CommitOrDiscard”语义
**Files Changed:** `Terrain.Editor/Services/Commands/HistoryManager.cs`

**Implementation:**
- `BeginCommand` 不再立即抓 before-state。
- `UpdateCommandRegion` 更名为 `MarkCommandChunks`。
- `CommitCommand` 调用 `PrepareForCommit()`，若无有效变更则直接丢弃，不入 undo 栈。

**Rationale:**
- 对齐 Godot 的“无变化不创建 Undo action”模式，避免历史污染。

---

### 3. Height/Paint 命令改为按 chunk 存储 before/after
**Files Changed:** `Terrain.Editor/Services/Commands/HeightEditCommand.cs`, `Terrain.Editor/Services/Commands/PaintEditCommand.cs`

**Implementation:**
- 两个命令都改为保存 `changedChunks` 列表（每块包含 region + before + after）。
- 提交阶段对每块做 `before == after` 过滤。
- Undo/Redo 采用逐 chunk 行拷贝回放。
- Paint 改为对 `MaterialIndexMap.GetRawData()` 做 row block copy，绕开 `GetPixel/SetPixel` 热路径。

**Rationale:**
- 固定粒度、稳定性能、内存增长与真实触达区域线性相关。

---

### 4. 编辑器调用顺序调整（先标记后写入）
**Files Changed:** `Terrain.Editor/Services/HeightEditor.cs`, `Terrain.Editor/Services/PaintEditor.cs`

**Implementation:**
- 在工具 `Apply` 前调用 `HistoryManager.Instance.MarkCommandChunks(...)`。
- 关键处补充注释说明“先标记，才能捕获真实 before-state”。

**Rationale:**
- 如果先写数据再标记，会把“修改后状态”误当 before-state，导致 Undo 错误。

---

## Decisions Made

### Decision 1: 固定 chunk 大小为 64
**Options Considered:**
1. 动态 chunk（复杂度高）
2. 固定 32（命令数量偏多）
3. 固定 64（平衡）

**Decision:** 选 64（与 Godot 参考实现一致）
**Trade-offs:** 大笔触场景下 chunk 数量受控；小笔触会有少量边际冗余。

### Decision 2: 无改动笔触丢弃，不入历史
**Decision:** 在 `PrepareForCommit()` 阶段过滤 empty/no-op
**Rationale:** 减少“Undo 但看不到变化”的噪音体验。

---

## What Worked ✅
1. **Chunk 首触达捕获 before-state**
   - Why it worked: 将抓取成本分散到笔触过程中，避免开始阶段卡顿尖峰。
2. **Paint raw buffer 行拷贝**
   - Why it worked: 消除逐像素 API 调用开销，路径更接近底层内存模型。
3. **统一 Height/Paint 历史语义**
   - Why it worked: 降低维护复杂度，后续功能扩展只需改一套交易模型。

---

## What Didn't Work ❌
1. **旧区域快照模型**
   - Why it failed: 影响区域在 Begin 阶段尚未收敛，容易退化为整图复制。
   - Don't try this again because: 在高分辨率地形下会直接反映为笔触卡顿。

---

## Code Quality Notes

### Performance
- 从“整图/大包围盒快照”切换为“触达 chunk 快照”。
- Paint 回放与抓取路径改为原始 RGBA 行拷贝。

### Testing
- 构建验证：`dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`（通过）

### Technical Debt
- 暂未实现 undo chunk 落盘缓存（本次按计划不做）。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 在真实编辑场景下做交互回归（长笔触、大笔刷、边界刷写）。
2. 增加最小化自动化测试（chunk 捕获与 no-op 过滤）。
3. 评估是否需要“可配置 chunk size”。

### Questions to Resolve
1. 是否需要将 chunk 大小提升为项目配置项（而非常量）？
2. 是否需要对超长会话引入磁盘缓存版 undo（类似 Godot image cache）？

---

## Session Statistics

**Files Changed:** 7  
**Lines Added/Removed:** +255/-175  
**Commits:** 1 (`cd8d5af`)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 新历史主干：`Terrain.Editor/Services/Commands/TerrainEditCommand.cs`
- chunk 跟踪器：`Terrain.Editor/Services/Commands/StrokeChunkTracker.cs`
- 历史入口：`Terrain.Editor/Services/Commands/HistoryManager.cs`
- Paint/Height 接入点：`Terrain.Editor/Services/PaintEditor.cs`, `Terrain.Editor/Services/HeightEditor.cs`

**Gotchas for Next Session:**
- `MarkCommandChunks` 必须在工具写入前调用。
- `PrepareForCommit()` 返回 false 时必须丢弃当前命令。
- Paint 回放直接写 raw RGBA buffer，修改格式时要同步 `BytesPerPixel` 逻辑。

---

## Links & References

### Related Sessions
- [2026-04-07-4-undo-redo-design](./2026-04-07-4-undo-redo-design.md)

### External Resources
- `E:\reference\godot_heightmap_plugin`（Undo/Redo chunk 事务参考）

### Code References
- `Terrain.Editor/Services/Commands/StrokeChunkTracker.cs`
- `Terrain.Editor/Services/Commands/TerrainEditCommand.cs`
- `Terrain.Editor/Services/Commands/PaintEditCommand.cs`
- `Terrain.Editor/Services/Commands/HeightEditCommand.cs`
- `Terrain.Editor/Services/Commands/HistoryManager.cs`

