---
doc_type: architecture
slug: brush-commands
scope: 笔刷系统和 Undo/Redo 命令模式
summary: 所有笔刷共享 BeginStroke/ApplyStroke/EndStroke 三阶段生命周期，HistoryManager 管理 Undo/Redo 栈
status: current
last_reviewed: 2026-04-20
tags: [editor, brush, undo-redo, command-pattern]
depends_on: [editor-services]
---

## 1. 定位与受众

本文档描述笔刷系统和 Undo/Redo 命令模式。读者是添加新笔刷类型、修改 Undo/Redo 行为、或调试笔触数据的人。

## 2. 结构与交互

```
BeginStroke(toolName)
    ├─ 创建 TerrainEditCommand（无快照）
    └─ StrokeChunkTracker 开始追踪

ApplyStroke(position, delta)
    ├─ StrokeChunkTracker.MarkCircle()（首次 touch 抓 before 快照）
    ├─ IHeightTool.Apply() / IPaintTool.Apply()
    └─ MarkDataDirty(channel)

EndStroke()
    ├─ 抓取 after 快照
    ├─ StrokeChunkTracker.GetRegions() → 过滤 no-op
    ├─ HistoryManager.CommitCommand()（非空才入栈）
    └─ SyncDataToGpu()
```

### 工具接口

```
IHeightTool (Raise/Lower/Smooth/Flatten)
    └─ Apply(ref HeightEditContext)

IPaintTool (Paint/Erase)
    └─ Apply(ref PaintEditContext)
```

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| BrushParameters | singleton | 全局 | 内存 |
| TerrainEditCommand | abstract | HistoryManager | Undo/Redo 栈 |
| HeightChunkDelta / PaintChunkDelta | record struct | 各 Command | 内存（压缩前/后） |
| StrokeChunkTracker | class | 当前笔触 | 内存（临时） |

## 4. 关键决策

- **Chunk 事务模型** → `2026-04-20-decision-chunk-transaction-undo.md`
- **Mark before Write**：先标记 chunk 再写入，before 快照不被当前笔触污染

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| TerrainEditCommand | `Terrain.Editor/Services/Commands/TerrainEditCommand.cs:12` | 基础命令抽象类 |
| MarkAffectedArea | `Terrain.Editor/Services/Commands/TerrainEditCommand.cs:29` | 标记受影响区域 |
| HeightEditCommand | `Terrain.Editor/Services/Commands/HeightEditCommand.cs:13` | 高度编辑命令 |
| PaintEditCommand | `Terrain.Editor/Services/Commands/PaintEditCommand.cs:13` | 材质绘制命令 |
| StrokeChunkTracker | `Terrain.Editor/Services/Commands/StrokeChunkTracker.cs:11` | 笔触 chunk 追踪器 |
| MarkCircle | `Terrain.Editor/Services/Commands/StrokeChunkTracker.cs:32` | 标记圆形区域 |
| GetRegions | `Terrain.Editor/Services/Commands/StrokeChunkTracker.cs:63` | 获取受影响 chunk 区域 |
| TerrainChunkRegion | `Terrain.Editor/Services/Commands/StrokeChunkTracker.cs:99` | chunk 区域 record struct |
| BrushParameters | `Terrain.Editor/Services/BrushParameters.cs:11` | 笔刷参数 singleton |
| IHeightTool | `Terrain.Editor/Services/IHeightTool.cs:68` | 高度工具接口 |
| IPaintTool | `Terrain.Editor/Services/IPaintTool.cs:130` | 绘制工具接口 |
| PaintBrushCore | `Terrain.Editor/Services/PaintBrushCore.cs:11` | 共享绘制逻辑 |
| ComputeSlopeMultiplier | `Terrain.Editor/Services/PaintBrushCore.cs:123` | 坡度过滤计算 |

## 6. 已知约束 / 边界情况

- Undo 栈上限 100 条 / 500MB，超出时最老命令被丢弃
- 空 stroke（before==after 的 chunks）不入栈
- TerrainDataChannel 目前只有 Height 和 MaterialIndex，ClimateMask 走单独的同步路径

## 7. 相关文档

- [editor-services.md](editor-services.md) — 服务层总览
- `2026-04-20-learning-chunk-based-undo-redo.md` — Chunk Undo/Redo 模式