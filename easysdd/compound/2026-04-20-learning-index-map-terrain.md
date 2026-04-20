---
doc_type: learning
feature: terrain-editor
status: current
tags: [index-map, splatmap, material, rendering, reference]
created: 2026-04-20
source: docs/log/learnings/index-map-terrain.md
---

# Index Map 材质索引技术

## 问题 / 背景

传统 SplatMap 只能映射 4 个纹理通道，无法支持大量材质种类。Unity IndexMapTerrain 项目提出了一种索引图（Index Map）方案，本 learning 记录其核心原理和适用场景。

## 解决方案 / 模式

Index Map 使用 RGBA 格式，每像素存储 4 个通道：
- **R**：材质索引（0-255，映射到 Texture2DArray 的 slice）
- **G**：权重（0-1，与同一像素其他材质权重归一化）
- **B**：3D 投影方向（4:4 编码，解决悬崖纹理拉伸）
- **A**：旋转角度（UV 随机旋转打破平铺重复）

采样方式：`Texture2DArray.Sample(sampler, float3(uv, sliceIndex))`，sliceIndex 由 R 通道决定。

## 关键要点

1. **256 种材质**：R 通道索引理论上限 256，远超传统 SplatMap 的 4 通道限制
2. **3D 投影编码**：B 通道 4:4 格式编码投影方向，shader 中根据方向旋转采样 UV，使纹理沿表面法线方向映射
3. **权重归一化**：多材质混合时权重需归一化，否则会出现亮度异常
4. **Texture2DArray**：所有材质纹理打包为数组，通过 sliceIndex 直接采样

## 何时使用

- 地形需要超过 4 种材质混合
- 悬崖面需要避免纹理拉伸
- 需要随机旋转打破平铺感

## 何时不用

- 材质种类 ≤ 4（传统 SplatMap 更简单）
- 内存极其受限（RGBA 比 RGB 多 25%）

## 常见错误

| ❌ 错误 | ✅ 正确 |
|---|---|
| 用 SplatMap 通道映射超过 4 种材质 | 用 IndexMap R 通道做索引 |
| 悬崖纹理直接平面采样 | 3D 投影方向编码到 B 通道 |
| 不做权重归一化 | Paint 时同步归一化同像素其他材质权重 |

## 性能考虑

- IndexMap RGBA 在 1/2 高度图分辨率下内存开销可接受（2048² → 4MB）
- Texture2DArray 采样是单次纹理查找，性能优于多 pass 混合
- 3D 投影增加少量 shader ALU，可接受的额外开销