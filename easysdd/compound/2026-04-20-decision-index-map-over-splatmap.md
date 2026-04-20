---
doc_type: decision
status: current
tags: [material, rendering, architecture, index-map]
created: 2026-04-20
---

# IndexMap 替代传统 SplatMap

## 背景

传统 SplatMap 每张纹理 4 通道，只能混合 4 种材质。本项目地形需要大量材质种类和 3D 投影能力。

## 决定

使用 IndexMap（RGBA8_UNorm）替代传统 SplatMap。R 通道存储材质索引（0-255），G 存储权重，B 存储 3D 投影方向，A 存储旋转角度。

## 备选方案

| 方案 | 优点 | 缺点 |
|---|---|---|
| **传统 SplatMap** | 简单、GPU 原生支持 | 4 种材质上限、无投影/旋转 |
| **多层 SplatMap** | 支持更多材质 | 通道浪费、内存翻倍 |
| **IndexMap（选用）** | 256 种材质、3D 投影、旋转 | 内存比单 SplatMap 大、shader 更复杂 |

## 理由

1. 256 种材质支持满足大型地形多样化需求
2. 3D 投影解决悬崖纹理拉伸，参考 Unity IndexMapTerrain
3. 随机旋转打破平铺重复感
4. 参考项目：Unity IndexMapTerrain

## 权衡

- 内存从单 SplatMap(RGBA8) 的 4MB 增至 IndexMap(RGBA8) 1/2 分辨率 4MB，实际内存开销接近
- Shader 采样逻辑更复杂（需要根据索引查 Texture2DArray slice）

## 影响

- MaterialIndexMap 成为材质系统的核心数据结构
- PaintEditor 和 ClimateRuleService 的输出都是 MaterialIndexMap
- 运行时和编辑器 shader 都需要 IndexMap 采样逻辑