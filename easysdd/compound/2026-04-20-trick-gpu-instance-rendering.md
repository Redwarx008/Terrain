---
doc_type: trick
type: performance
status: current
tags: [rendering, gpu, instancing, buffer]
created: 2026-04-20
---

# GPU 实例化渲染：预分配 InstanceBuffer

## 处方

地形渲染使用 `DrawIndexedInstanced` 单次绘制调用。InstanceBuffer 预分配固定大小（max 200 instances），避免每帧创建/销毁 GPU Buffer。

## 做法

1. TerrainRenderObject 持有预创建的 InstanceBuffer（固定大小）
2. TerrainProcessor 每 frame 把可见 chunk 的 TerrainChunkNode 数据写入 InstanceBuffer
3. 不可见 chunk 不写入，DrawIndexedInstanced 参数用实际可见数量
4. 不需要每帧创建/销毁 Buffer

## 反模式

- ❌ `new Buffer()` + `buffer.Dispose()` 每帧循环 → GPU 内存抖动 + GC 压力
- ❌ 动态调整 Buffer 大小 → 频繁 GPU 分配

## 背景

最初实现中 `TerrainProcessor.UploadChunkBuffer()` 每帧创建/销毁 Buffer，导致 GPU 内存抖动。预分配后完全消除此开销。

## 适用

- 实例数量有合理上限的场景（地形 chunk 数量有限）
- 每帧实例数据变化但最大数量不变