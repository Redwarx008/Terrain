# Chunk-Based Undo/Redo（地形编辑）

**Topic**: 笔触事务的 chunk 化历史记录  
**Date**: 2026-04-07  
**Related Sessions**: [2026-04-07-5](../2026/04/07/2026-04-07-5-chunk-based-undo-redo-implementation.md)

---

## Problem / Context

- 区域快照模型在笔触开始时容易退化为整图复制，导致 Paint Mode 明显卡顿。
- Height/Paint 的 Undo 需要统一语义，避免两套行为和性能特性分裂。

---

## Solution / Pattern

```csharp
// 1) BeginStroke: 仅开启命令事务，不抓快照
HistoryManager.Instance.BeginCommand(command);

// 2) ApplyStroke: 先标记 chunk（首次触达时抓 before）
HistoryManager.Instance.MarkCommandChunks(pixelX, pixelZ, brushRadius);
tool.Apply(ref context);

// 3) EndStroke: 统一抓 after，过滤 unchanged chunk，no-op 丢弃
HistoryManager.Instance.CommitCommand();
```

---

## Key Insights

### 1. “先标记后写入”是正确性关键
- 如果先写入再标记，before-state 可能被污染为修改后数据，Undo 会失真。

### 2. 固定 chunk 比动态区域更稳定
- 性能与内存增长由“触达 chunk 数”决定，避免包围盒持续扩张带来的不可控成本。

### 3. no-op 过滤能显著提升历史质量
- 空笔触不入栈，避免“Undo 但无视觉变化”的体验噪声。

---

## When to Use

- 实时笔刷编辑（每帧都会写数据）的 Undo/Redo。
- 数据尺寸较大，整图快照代价高的场景。

---

## When NOT to Use

- 低频且全局一次性变更（例如重建整张图）且可接受离线耗时时。
- 数据总量极小、实现复杂度优先级更高时。

---

## Common Mistakes

### ❌ Mistake 1: 在 BeginStroke 立即抓整图 before-state
**What to avoid:**
- 在受影响区域尚不明确时执行大快照。

**Why it's bad:**
- 高频交互会出现明显卡顿峰值。

**Correct approach:**
- 首次触达 chunk 时抓 before-state，提交时抓 after-state。

### ❌ Mistake 2: 不过滤 unchanged chunk
**What to avoid:**
- 所有触达 chunk 都入历史，不比较 before/after。

**Why it's bad:**
- 增加内存和回放成本，并污染 Undo 栈。

**Correct approach:**
- commit 阶段做 chunk 级 diff，未变化块直接丢弃。

---

## Performance Considerations

- Paint 通道优先使用原始 buffer 的行拷贝，避免 per-pixel API 循环。
- chunk size 建议从 `64` 起步；过小会增加命令元数据，过大会放大边际冗余。

---

## Related Patterns

- [Index Map Terrain](index-map-terrain.md)

---

## References

- Godot Heightmap Plugin (`E:\reference\godot_heightmap_plugin`)
- [Session: 2026-04-07-5](../2026/04/07/2026-04-07-5-chunk-based-undo-redo-implementation.md)

