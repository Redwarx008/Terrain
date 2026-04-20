---
doc_type: architecture
slug: shader-pipeline
scope: 运行时和编辑器的着色器管线：Displacement、Diffuse、Compute Shader
summary: 三阶段 Compute 构建 LodLookup/LodMap/NeighborMask，Displacement 做裂缝修复，Diffuse 做 IndexMap 材质混合
status: current
last_reviewed: 2026-04-20
tags: [shader, compute, sdsl, rendering]
depends_on: [render-pipeline, climate-material]
---

## 1. 定位与受众

本文档描述地形着色器管线的完整流程。读者是修改材质混合逻辑、LOD 裂缝修复、或添加新 Compute Shader 时需要理解 Shader 间数据流的人。

## 2. 结构与交互

### Runtime Shader 流程

```
TerrainBuildLodLookup (Compute) → LodLookupBuffer
TerrainBuildLodMap (Compute)     → LodMapTexture
TerrainBuildNeighborMask (Compute) → InstanceBuffer.w

MaterialTerrainDisplacement (Vertex)
    ├─ 读取 InstanceBuffer (NodeInfo + StreamInfo + SplatInfo)
    ├─ 高度采样 (HeightmapArray + VT streaming)
    ├─ 裂缝修复 (NeighborMask → mid-point snap)
    └─ 输出 displaced position + streams

MaterialTerrainDiffuse (Pixel)
    ├─ 法线计算 (高度差分)
    ├─ IndexMap 采样 (4 层双线性混合 + 镜像采样)
    ├─ 材质纹理采样 (Texture2DArray, 3D 投影 + 旋转)
    └─ 输出 matDiffuse + normalWS
```

### Editor 额外 Shader

```
EditorTerrainBuildSplatMap (Compute)
    Input: ClimateMask + ClimateRules + Heightmap
    Output: MaterialIndexMap (RGBA)
    流程: 4×4 altitude average → central difference slope → rule match → encode

EditorTerrainDisplacement (Vertex) — 编辑器版高度位移
EditorTerrainDiffuse (Pixel) — 编辑器版材质混合
```

## 3. 数据与状态

| 数据 | 类型 | 归属 | Shader |
|---|---|---|---|
| InstanceBuffer | Buffer | TerrainRenderObject | Compute + Vertex |
| LodLookupBuffer | Buffer | TerrainRenderObject | Compute |
| LodMapTexture | Texture | TerrainRenderObject | Compute + Vertex |
| HeightmapArray | Texture | TerrainRenderObject | Vertex + Pixel |
| IndexMapArray / SplatMapArray | Texture | TerrainRenderObject | Pixel |
| MaterialSlots (albedo/normal) | Texture2DArray | RuntimeMaterialManager | Pixel |

## 4. 关键决策

- **LOD 裂缝修复：细粒度向粗粒度对齐** → `2026-04-20-trick-lod-crack-snap.md`
- **IndexMap RGBA 格式** → `2026-04-20-decision-index-map-over-splatmap.md`

## 5. 代码锚点

### Runtime Shader

| 锚点 | 文件 | 说明 |
|---|---|---|
| TerrainBuildLodLookup | `Terrain/Effects/Build/TerrainBuildLodLookup.sdsl:3` | LOD 查找表构建 |
| TerrainBuildLodMap | `Terrain/Effects/Build/TerrainBuildLodMap.sdsl:3` | 逐像素 LOD 纹理 |
| TerrainBuildNeighborMask | `Terrain/Effects/Build/TerrainBuildNeighborMask.sdsl:3` | 邻居 LOD 差值 |
| MaterialTerrainDisplacement | `Terrain/Effects/Material/TerrainDisplacement.sdsl:3` | 高度位移 + 裂缝修复 |
| MaterialTerrainDiffuse | `Terrain/Effects/Material/TerrainDiffuse.sdsl:3` | 材质混合 |
| TerrainHeightStream | `Terrain/Effects/Stream/TerrainHeightStream.sdsl:3` | 高度流声明 |
| TerrainHeightParameters | `Terrain/Effects/Stream/TerrainHeightParameters.sdsl:3` | 高度参数 + 采样工具 |
| TerrainForwardShadingEffect | `Terrain/Effects/TerrainForwardShadingEffect.sdfx` | Effect 组合 |

### Editor Shader

| 锚点 | 文件 | 说明 |
|---|---|---|
| EditorTerrainBuildSplatMap | `Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl:3` | GPU Compute 生成 IndexMap |
| EditorTerrainDisplacement | `Terrain.Editor/Effects/EditorTerrainDisplacement.sdsl:3` | 编辑器高度位移 |
| EditorTerrainDiffuse | `Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl:3` | 编辑器材质混合 |
| EditorTerrainHeightStream | `Terrain.Editor/Effects/EditorTerrainHeightStream.sdsl:3` | 编辑器高度流 |
| EditorTerrainHeightParameters | `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl:3` | 编辑器高度参数 |

## 6. 已知约束 / 边界情况

- Editor 使用 sliced heightmap（最多 8 个 Texture2D），Runtime 使用 Texture2DArray + 流式加载
- Compute Shader 的线程组大小固定：LodLookup=64, LodMap=8×8, NeighborMask=64
- 3D 投影编码为 B 通道 4:4 格式，shader 中解码旋转采样 UV

## 7. 相关文档

- [render-pipeline.md](render-pipeline.md) — Compute 调度流程
- [climate-material.md](climate-material.md) — SplatMap Compute 输入输出