---
doc_type: trick
type: rendering
status: current
tags: [lod, crack, displacement, shader]
created: 2026-04-20
---

# LOD 裂缝修复：细粒度向粗粒度对齐

## 处方

LOD 边界处只有细粒度 chunk 的边缘顶点向粗粒度 chunk 的采样点对齐（snap），粗粒度 chunk 不变形。NeighborMask 8-bit per edge 编码差值，Displacement shader 做 mid-point snap。

## 做法

1. GPU Compute（TerrainBuildNeighborMask）：对比 chunk 与邻居的 LOD 差值，写入 InstanceBuffer.w
2. 编码：8-bit per edge（left/right/top/bottom），4 个 byte 打包到 int
3. Displacement shader：边缘中点根据 neighbor delta 做 snap——如果邻居 LOD 更粗，中点对齐到粗 LOD 的采样点
4. 法线计算同步到同一 snap 规则，避免法线缝隙

## 反模式

- ❌ 粗粒度 chunk 也变形向细粒度对齐 → 粗 chunk 内部顶点全部位移，视觉错误
- ❌ 2-bit per edge（4 级差值不够）→ 8-bit per edge 支持 256 级
- ❌ 角点额外修正 → 角点自然落在共享采样点上，不需要额外处理

## 适用

- 四叉树 LOD 离散切换的任何场景
- tessellation-free terrain rendering