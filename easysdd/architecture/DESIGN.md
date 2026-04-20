---
doc_type: architecture
slug: DESIGN
scope: 项目架构总入口：术语、子系统索引、关键决策、约束
summary: ImGui 编辑器 + 运行时组件双端架构，10 个子系统，6 项关键决策
status: current
last_reviewed: 2026-04-20
tags: [architecture, index]
depends_on: []
---

# Terrain 地形编辑器 架构总入口

## 0. 术语

| 术语 | 定义 |
|---|---|
| **ClimateMask** | R8 格式气候蒙版，1/4 高度图分辨率，每像素存储一个气候 ID |
| **MaterialIndexMap** | RGBA 材质索引图（1/2 高度图分辨率），R=材质索引 G=权重 B=3D 投影方向 A=旋转角度 |
| **IndexMap** | 索引图模式：用 R 通道存 256 种材质索引，区别于传统 SplatMap 的 4 通道映射 |
| **TerrainDataChannel** | 统一数据通道枚举，`MarkDataDirty(channel)` 驱动 GPU 数据同步 |
| **Chunk 事务** | Undo/Redo 基本单位：笔触期间标记 chunk，提交时抓取 before/after 快照 |
| **TerrainPageKey** | 虚拟纹理页标识（x, y, mip），流式加载的最小调度单位 |
| **TerrainChunkNode** | GPU 实例数据（Int4 NodeInfo + Int4 StreamInfo + Vector4 SplatInfo），每个可见 chunk 一个 |
| **.terrain 文件** | 二进制地形导出格式（v3），含 MinMaxErrorMap、Heightmap/SplatMap 分层 mip |

## 1. 定位与受众

本文档是架构总入口，供 feature-design 定位子系统、issue-analyze 理解模块边界、新人了解系统全貌。

## 2. 子系统 / 模块索引

| 子系统 | 状态 | 详细文档 |
|---|---|---|
| Core 组件 + Processor | ✅ | [core-component.md](core-component.md) |
| 流式加载 + 虚拟纹理 | ✅ | [streaming.md](streaming.md) |
| 渲染管线（Runtime） | ✅ | [render-pipeline.md](render-pipeline.md) |
| 编辑器服务层 | ✅ | [editor-services.md](editor-services.md) |
| 笔刷 + Undo/Redo | ✅ | [brush-commands.md](brush-commands.md) |
| 气候蒙版 + 材质系统 | ✅ | [climate-material.md](climate-material.md) |
| 着色器管线 | ✅ | [shader-pipeline.md](shader-pipeline.md) |
| 导出系统 | ✅ | [export.md](export.md) |
| UI 面板体系 | ✅ | [ui-panels.md](ui-panels.md) |
| 项目持久化（TOML） | ✅ | [project-persistence.md](project-persistence.md) |

## 3. 关键架构决定

| # | 决定 | 理由 | 详细文档 |
|---|---|---|---|
| 1 | IndexMap 而非传统 SplatMap | 支持 256 种材质 + 3D 投影 + 随机旋转 | [decision-index-map](../compound/2026-04-20-decision-index-map-over-splatmap.md) |
| 2 | ClimateMask R8 1/4 分辨率间接映射 | 规则驱动比直接绘制更高效，1/4 分辨率节省内存 | [decision-climate-mask](../compound/2026-04-20-decision-climate-mask-r8.md) |
| 3 | Chunk 事务模型 Undo/Redo | 避免整图快照，笔触级增量 | [decision-chunk-undo](../compound/2026-04-20-decision-chunk-transaction-undo.md) |
| 4 | TOML 项目持久化 | 人类可编辑、可版本控制 | [decision-toml](../compound/2026-04-20-decision-toml-project-persistence.md) |
| 5 | 独立 material_descriptor.toml 导出 | 运行时不应依赖编辑器项目文件 | [decision-export](../compound/2026-04-20-decision-material-descriptor-export.md) |
| 6 | SplatMap 固定 1/2 高度图分辨率 | 内存从 16MB 降至 4MB，视觉差异可忽略 | [decision-half-res](../compound/2026-04-20-decision-splatmap-half-resolution.md) |

## 4. 已知约束 / 硬边界

- 气候蒙版→高度图坐标转换统一 ×4，→材质索引图统一 ×2
- 导出 padding：HeightMap=2, SplatMap=1
- 所有项目路径使用相对路径（相对于 .toml 所在目录）
- GPU Texture2DArray 最多绑定 8 个 slice（编辑器 sliced heightmap 限制）
- 运行时流式加载：HeightMap 与 IndexMap 页同步驻留
- Undo/Redo 内存上限 500MB / 100 条命令

## 5. 数据流概览

**编辑器数据流：**

```
Heightmap PNG → TerrainManager → EditorTerrainEntity → GPU Upload
                                                    ↓
ClimateMask(R8) → ClimateRuleService → GPU Compute → MaterialIndexMap(RGBA)
                                                    ↓
EditorTerrainRenderFeature → LOD Selection → ChunkNodeBuffer → DrawIndexedInstanced
                                                    ↓
Export: .terrain (binary) + material_descriptor.toml (TOML)
```

**运行时数据流：**

```
.terrain file → TerrainFileReader → TerrainStreamingManager → GpuVirtualTextureArray(LRU)
                                                              ↓
                                                    TerrainQuadTree.Select() → ChunkNodeBuffer
                                                              ↓
                                                    TerrainRenderFeature → DrawIndexedInstanced
```

## 6. 相关文档

- [easysdd/compound/](../compound/) — learning / trick / decision 沉淀文档
- [docs/ARCHITECTURE_OVERVIEW.md](../../docs/ARCHITECTURE_OVERVIEW.md) — 项目原有架构文档（未纳入 easysdd 体系）