---
doc_type: trick
type: performance
status: current
tags: [gpu-upload, incremental, data-sync]
created: 2026-04-20
---

# 增量 GPU 数据上传：DirtyRegion 追踪

## 处方

编辑器中 GPU 纹理上传只传输脏区域（dirty region），而非整张纹理。`MarkDataDirty(channel)` → `DirtyRegionTracker` 记录边界 → 上传时只传输 affected region。

## 做法

1. 每次笔触操作后 `MarkDataDirty(TerrainDataChannel.Height)` 等
2. DirtyRegionTracker 记录脏区域边界（minX, minY, maxX, maxY）
3. 渲染帧上传时：`GraphicsDevice.UpdateSubresource(texture, 0, dirtyRegion, dataPtr, rowPitch)`
4. 上传后重置 dirty region

## 反模式

- ❌ 每帧上传整张高度图纹理 → 2048² × 2 bytes = 8MB 每帧
- ❌ 每次 ApplyStroke 都上传 → 60fps 时 GPU 带宽饱和

## 适用

- 大纹理局部编辑场景
- 编辑器模式（非实时渲染可以接受 1 帧延迟）