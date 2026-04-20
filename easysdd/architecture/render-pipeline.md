---
doc_type: architecture
slug: render-pipeline
scope: 运行时地形渲染管线：LOD 选择、GPU Compute、实例化绘制
summary: QuadTree.Select() 收集可见 chunk → Compute Shader 构建 LodLookup/LodMap/NeighborMask → DrawIndexedInstanced
status: current
last_reviewed: 2026-04-20
tags: [rendering, lod, compute-shader, gpu-instancing]
depends_on: [core-component, streaming, shader-pipeline]
---

## 0. 术语

| 术语 | 定义 |
|---|---|
| SSE | Screen Space Error，像素级几何误差阈值 |
| LodLookup | chunk→slice 索引查找表（Buffer） |
| LodMap | 逐像素 LOD 纹理（Texture） |
| NeighborMask | 8-bit/edge 的邻居 LOD 差值，用于裂缝修复 |

## 1. 定位与受众

本文档描述运行时渲染管线。读者是调试 LOD 切换、裂缝、或绘制性能时需要理解从 QuadTree 到 DrawCall 全链路的人。

## 2. 结构与交互

```
TerrainRenderFeature.PrepareRenderFrames()
    ├─ TerrainQuadTree.Select()
    │   ├─ Frustum cull
    │   ├─ SSE 计算（MinMaxErrorMap）
    │   └─ 收集可见 chunk → TerrainChunkNode[]
    ├─ TerrainStreamingManager 请求缺失页
    └─ 收集可见 chunk

TerrainComputeDispatcher.Dispatch()
    ├─ TerrainBuildLodLookup   → LodLookupBuffer
    ├─ TerrainBuildLodMap       → LodMapTexture
    └─ TerrainBuildNeighborMask → InstanceBuffer.w (8-bit/edge)

DrawIndexedInstanced(chunkCount)
    └─ MaterialTerrainDisplacement + MaterialTerrainDiffuse
```

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| TerrainChunkNode[] | struct[] | TerrainRenderObject | GPU Buffer |
| LodLookupBuffer | Buffer | TerrainRenderObject | GPU |
| LodMapTexture | Texture | TerrainRenderObject | GPU |
| MinMaxErrorMap[] | class[] | TerrainComponent | 内存 |

## 4. 关键决策

- **LOD 裂缝修复：细粒度向粗粒度对齐** → `2026-04-20-trick-lod-crack-snap.md`
- **GPU Compute 构建 LodLookup/LodMap/NeighborMask** 而非 CPU 逐帧计算

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| TerrainRenderFeature | `Terrain/Rendering/TerrainRenderFeature.cs:26` | 根渲染特性 |
| PrepareTerrainDraw | `Terrain/Rendering/TerrainRenderFeature.cs:635` | 绘制前准备 |
| Draw (instanced) | `Terrain/Rendering/TerrainRenderFeature.cs:183` | 实例化绘制 |
| TerrainQuadTree | `Terrain/Rendering/TerrainQuadTree.cs:11` | 四叉树 LOD 选择 |
| Select | `Terrain/Rendering/TerrainQuadTree.cs:54` | 遍历 + frustum cull + SSE |
| TerrainComputeDispatcher | `Terrain/Rendering/TerrainComputeDispatcher.cs:13` | GPU Compute 调度 |
| Dispatch | `Terrain/Rendering/TerrainComputeDispatcher.cs:47` | 三阶段 Compute |
| TerrainRenderObject | `Terrain/Rendering/TerrainRenderObject.cs:13` | GPU 资源容器 |
| UpdateChunkNodeData | `Terrain/Rendering/TerrainRenderObject.cs:170` | 上传 ChunkNode 数据 |

## 6. 已知约束 / 边界情况

- MaxVisibleChunkInstances 默认 65536，超出则裁剪远处 chunk
- MaxScreenSpaceErrorPixels 默认 8.0，值越小 LOD 越精细但绘制更多 chunk
- Shadow pass 复用主 pass 的 LOD 选择结果

## 7. 相关文档

- [core-component.md](core-component.md) — TerrainComponent 持有 QuadTree
- [streaming.md](streaming.md) — QuadTree 驱动页请求
- [shader-pipeline.md](shader-pipeline.md) — Compute Shader 细节