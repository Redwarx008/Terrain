---
doc_type: decision
status: current
tags: [splatmap, memory, performance, architecture]
created: 2026-04-20
---

# SplatMap 固定 1/2 高度图分辨率

## 背景

SplatMap（MaterialIndexMap）与 HeightMap 1:1 分辨率时，2048×2048 地形的 SplatMap 内存达 16MB（RGBA8）。材质索引不需要高度图级别的精度。

## 决定

SplatMap 固定为 HeightMap 的 1/2 分辨率，内存从 16MB 降至 4MB。

## 备选方案

| 方案 | 内存（2048²地形） | 视觉质量 |
|---|---|---|
| **1:1 分辨率** | 16MB | 理论最优 |
| **1/2 分辨率（选用）** | 4MB | 视觉差异可忽略 |
| **1/4 分辨率** | 1MB | 材质边界模糊 |

## 理由

1. 材质索引权重是低频信息，1/2 分辨率视觉差异可忽略
2. 内存节省 75%（16MB → 4MB）
3. 固定比例简化坐标转换逻辑（统一 ×2）
4. 全链路一致：Editor、Preprocessor、File Format、Runtime Streaming、Shader

## 权衡

- 固定比例不支持可变分辨率（如果将来某些地形需要 1:1）
- 全链路修改：.terrain 文件版本 v2→v3、新增 SplatMapResolutionRatio 字段

## 影响

- .terrain 文件格式升至 v3（Reserved1 → SplatMapResolutionRatio）
- Runtime TerrainChunkNode 扩展 SplatInfo 字段
- 独立 SplatMap VT 页网格（页数为 HeightMap 的 1/4）
- Shader 中 splatPageLocalPos 需要 2x 缩放
- Editor MaterialIndexMap 创建时尺寸减半，笔刷坐标缩放 ×2