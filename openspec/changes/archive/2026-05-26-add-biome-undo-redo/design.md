## Context

当前 undo/redo 系统基于 chunk 快照事务模型（参见 [chunk-based-undo-redo.md](../../../docs/log/learnings/chunk-based-undo-redo.md)）。HeightEditor 已完整集成：`BeginStroke` → `BeginCommand` → `MarkCommandChunks` → `CommitCommand`。BiomeEditor 在 ADR-012 规则层重构时替换了旧 PaintEditor，但遗漏了 HistoryManager 集成——目前是纯 fire-and-forget 写入，Ctrl+Z 无任何效果。

`BiomeMask` 是 `byte[]`（R8 格式，1/2 分辨率），比 `HeightDataCache` 的 `ushort[]` 更简单。复用现有 `StrokeChunkTracker`（64×64 chunk）和 `TerrainEditCommand` 基类即可，无需引入新抽象。

## Goals / Non-Goals

**Goals:**
- Biome 笔刷支持 Ctrl+Z 撤销 / Ctrl+Y 重做，与 Height 编辑体验一致
- 复用现有 `HistoryManager` 和 `TerrainEditCommand` 基类，不加新抽象
- "先标记后写入" 顺序保持正确性
- 无改动笔触不入历史（与 Height 一致）

**Non-Goals:**
- 不改变 BiomeMask 数据结构
- 不修改 GPU SplatMap 生成管线
- 不添加 "取消当前笔触"（Escape）功能
- 不添加跨会话的磁盘持久化 undo

## Decisions

### D-1: 克隆 HeightEditCommand 模式，不做泛型抽象

**选择:** 新建 `BiomeEditCommand : TerrainEditCommand`，独立实现 byte[] 的 chunk 拷贝/回放逻辑。

**放弃的方案:** 泛型 `TerrainEditCommand<T>` 参数化数据类型。
**理由:** 当前只有两个命令类（Height + Biome），泛型化的收益不足以抵消复杂度。byte 和 ushort 的语义不同（biome ID vs 高度值），强行统一会在未来 biome 逻辑变复杂时成为阻碍。两个具体类比一个泛型类更易理解和修改。

### D-2: BiomeEditor 改为 BeginStroke/EndStroke 生命周期

**选择:** 添加 `BeginStroke(worldPosition, biomeId, terrainManager)` 和 `EndStroke()` 方法，与 HeightEditor 接口对齐。

**放弃的方案:** 在 `EmbeddedStrideViewportGame` 中直接创建 BiomeEditCommand。
**理由:** 保持服务封装——BiomeEditor 负责自己的命令生命周期，Viewport 只做调度。

### D-3: TerrainDataChannel 加 Biome 成员

**选择:** 在 `TerrainDataChannel` 枚举添加 `Biome`。

**理由:** `BiomeEditCommand.AffectedChannel` 需要返回一个通道标识。虽然当前 `MarkDataDirty` 对 biome 走的是 `MarkBiomeMaskDirty()` 单独路径，但在命令层面语义统一。如果未来做 `TerrainDataChannel.Biome` 的 dirty 标记统一化，无需再次改动。

### D-4: Undo/Redo 后的重生成策略

**选择:** Undo/Redo 回放 chunk 数据后，调用 `TerrainManager.MarkBiomeMaskDirty()` + 对每个 changed chunk 调用 `RegenerateMaterialIndices()`。

**理由:** BiomeMask 变更需要触发 GPU SplatMap 重新计算。沿用 HeightEditCommand 的 `MarkDataDirty` 模式，但走 biome 的脏标记路径。

## Risks / Trade-offs

- **[低风险] biome 数据尺寸小** — BiomeMask 是 1/2 分辨率 × 1 byte/像素，单 chunk（64×64）仅 4KB，内存压力远低于 Height 的 8KB/chunk
- **[已知限制] 只记录 byte ID 快照，不记录规则计算结果** — Undo 恢复的是 biome ID 分布，不是 SplatMap 像素。这是正确的行为（SplatMap 是 biome ID 的派生数据），但值得明确
