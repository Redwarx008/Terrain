---
doc_type: architecture
slug: climate-material
scope: 气候蒙版驱动材质索引系统：ClimateMask → GPU Compute → MaterialIndexMap
summary: ClimateMask(R8, 1/4) 存气候 ID，ClimateRuleService 管理规则栈，GPU Compute 从高度/坡度/季节求值生成 MaterialIndexMap(RGBA, 1/2)
status: current
last_reviewed: 2026-04-20
tags: [climate, material, gpu-compute, splatmap]
depends_on: [shader-pipeline, editor-services]
---

## 1. 定位与受众

本文档描述气候蒙版→材质索引的数据管线。读者是添加新规则类型、调试材质混合、或修改 Compute Shader 时需要理解端到端流程的人。

## 2. 结构与交互

```
ClimateMask (R8, 1/4 heightmap)
    ↓ 每像素 = climate_id (0-255)
ClimateRuleService
    ↓ 规则栈：altitude + slope + season → material_slot_index
EditorTerrainBuildSplatMap (GPU Compute)
    ↓
MaterialIndexMap (RGBA, 1/2 heightmap)
    ↓ R=material_index, G=weight, B=projection_dir, A=rotation
MaterialSlotManager
    ↓ Texture2DArray (albedo + normal per slot)
Shader Sampling (EditorTerrainDiffuse.sdsl)
```

### 坐标转换

| From | To | 系数 |
|---|---|---|
| ClimateMask | Heightmap | ×4 |
| ClimateMask | MaterialIndexMap | ×2 |
| MaterialIndexMap | Heightmap | ×2 |

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| ClimateMask | byte[] | TerrainManager | 内存 + GPU Texture |
| ClimateDefinition[] | class[] | ClimateRuleService | TOML 配置 |
| ClimateRuleLayer[] | class[] | ClimateRuleService | TOML 配置 |
| MaterialIndexMap | class (uint[]) | TerrainManager | 内存 + GPU Texture |
| MaterialSlot[] | class[] | MaterialSlotManager | TOML 配置 + GPU Texture2DArray |

## 4. 关键决策

- **ClimateMask R8 1/4 间接映射** → `2026-04-20-decision-climate-mask-r8.md`
- **IndexMap 替代传统 SplatMap** → `2026-04-20-decision-index-map-over-splatmap.md`
- **SplatMap 固定 1/2 分辨率** → `2026-04-20-decision-splatmap-half-resolution.md`

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| ClimateMask | `Terrain.Editor/Services/ClimateMask.cs:11` | R8 气候蒙版数据 |
| GetValue / SetValue | `Terrain.Editor/Services/ClimateMask.cs:31` / `:39` | 像素读写 |
| ClimateRuleService | `Terrain.Editor/Services/ClimateRuleService.cs:34` | 规则栈管理 singleton |
| GetRulesForClimate | `Terrain.Editor/Services/ClimateRuleService.cs:186` | 按 climate ID 查规则 |
| MaterialIndexMap | `Terrain.Editor/Services/MaterialIndexMap.cs:47` | RGBA 材质索引图 |
| MaterialPixel | `Terrain.Editor/Services/MaterialIndexMap.cs:11` | 像素结构体 |
| MaterialSlotManager | `Terrain.Editor/Services/MaterialSlotManager.cs:17` | 256 槽位 singleton |
| RebuildMaterialArrays | `Terrain.Editor/Services/MaterialSlotManager.cs:205` | GPU 纹理数组重建 |
| EditorTerrainSplatMapComputeDispatcher | `Terrain.Editor/Rendering/EditorTerrainRenderFeature.cs:885` | SplatMap Compute 调度 |
| EditorTerrainBuildSplatMap | `Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl` | GPU Compute Shader |

## 6. 已知约束 / 边界情况

- 规则求值在 GPU Compute 中，规则数量上限受 Structured Buffer 大小限制
- ClimateMask 1/4 分辨率意味着同一气候区域内所有像素共享同一 climate_id
- 季节过滤由 `EditorState.ActiveSeason` 驱动，运行时不支持

## 7. 相关文档

- [shader-pipeline.md](shader-pipeline.md) — Compute Shader 细节
- [editor-services.md](editor-services.md) — TerrainManager 协调