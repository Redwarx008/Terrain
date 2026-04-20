---
doc_type: learning
feature: terrain-editor
status: current
tags: [undo-redo, command-pattern, performance, brush]
created: 2026-04-20
source: docs/log/learnings/chunk-based-undo-redo.md
---

# Chunk 事务模型 Undo/Redo

## 问题 / 背景

区域快照式 Undo/Redo 在笔触开始阶段容易退化为整图复制，导致 Paint 模式卡顿。需要一个内存可控、性能稳定的方案。

## 解决方案 / 模式

Chunk 事务模型：笔触期间标记受影响的 chunk，提交时仅抓取 before/after 快照。

```
BeginStroke:  创建命令（无快照）
ApplyStroke:  markChunk()（首次 touch 抓 before 快照），然后应用编辑
EndStroke:    抓 after 快照，过滤 no-op chunks，非空才入栈
```

## 关键要点

1. **先标记再写入**（Mark before Write）：before 快照必须在写入之前抓取，否则当前笔触会污染 before 状态
2. **固定 chunk 比动态区域更稳定**：固定大小的 chunk 让内存开销可预测，性能与 touched chunk 数量线性相关
3. **no-op 过滤提升历史质量**：空笔触和未修改 chunk 不入栈，避免 Undo 栈膨胀

## 何时使用

- 任何需要 Undo/Redo 的编辑器（高度、材质、蒙版）
- 大尺寸画布上局部编辑场景

## 何时不用

- 整图批量操作（如全局高度偏移）——此时 before/after 就是整图快照，chunk 模型不增加价值
- 非交互式批处理（不需要 Undo）

## 常见错误

| ❌ 错误 | ✅ 正确 |
|---|---|
| 先写入数据再标记 chunk | 先 markChunk()（抓 before），再写入 |
| 每次 ApplyStroke 都做全图 diff | 只对 touched chunks 做 before/after 对比 |
| 空 stroke 也入栈 | EndStroke 过滤 before==after 的 chunks |
| 用动态区域快照 | 固定 chunk 大小，内存和性能可预测 |

## 性能考虑

- 内存上限 500MB / 100 条命令
- 性能与 touched chunk 数量线性相关，与画布总大小无关
- StrokeChunkTracker 去重：同一 chunk 在同一 stroke 中只标记一次