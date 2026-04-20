---
doc_type: decision
status: current
tags: [undo-redo, performance, architecture]
created: 2026-04-20
---

# Chunk 事务模型 Undo/Redo

## 背景

区域快照式 Undo/Redo 在笔触开始阶段容易退化为整图复制（Paint 模式），导致交互卡顿。

## 决定

采用 Chunk 事务模型：笔触期间标记受影响的 chunk，提交时仅抓取 before/after 快照。参考 Godot heightmap 插件。

## 备选方案

| 方案 | 优点 | 缺点 |
|---|---|---|
| **全图快照** | 实现简单 | 大地形内存爆炸 |
| **区域快照** | 比全图好 | Paint 模式仍退化为近全图 |
| **Chunk 事务（选用）** | 内存可控、性能稳定 | 实现更复杂 |

## 理由

1. 固定 chunk 大小让内存开销可预测，与画布总大小解耦
2. "Mark before write" 保证 before 快照不被当前笔触污染
3. no-op 过滤（before==after 的 chunk 不入栈）保持历史栈干净
4. 参考：Godot heightmap 插件的 "mark region during stroke, snapshot at commit" 模式

## 权衡

- 实现更复杂（StrokeChunkTracker 去重、命令压缩）
- 优势：性能与 touched chunk 数量线性相关，不再与画布大小耦合

## 影响

- HistoryManager、TerrainEditCommand、StrokeChunkTracker 构成 Undo/Redo 三层
- 所有笔刷（Height/Paint/Climate）共享同一笔触生命周期
- 内存上限 500MB / 100 条命令