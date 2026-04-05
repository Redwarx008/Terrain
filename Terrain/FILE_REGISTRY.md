# Terrain Core - 文件注册表

**命名空间:** `Terrain`
**职责:** 运行时地形组件和渲染系统

---

## 核心组件

| 文件 | 职责 |
|------|------|
| `Core/TerrainComponent.cs` | 地形组件主入口，附加到实体 |
| `Core/TerrainProcessor.cs` | 地形处理器，管理系统生命周期 |

## 渲染系统

| 文件 | 职责 |
|------|------|
| `Rendering/TerrainRenderFeature.cs` | 渲染特性，注册到 Stride 渲染管线 |
| `Rendering/TerrainRenderObject.cs` | 渲染对象，管理绘制数据 |
| `Rendering/TerrainQuadTree.cs` | 四叉树 LOD 结构 |
| `Rendering/TerrainComputeDispatcher.cs` | Compute Shader 调度器 |
| `Rendering/TerrainWireframeModeController.cs` | 线框模式控制器 |
| `Rendering/TerrainWireframeStageSelector.cs` | 线框渲染阶段选择器 |

## 材质系统

| 文件 | 职责 |
|------|------|
| `Rendering/Materials/MaterialTerrainDiffuseFeature.cs` | 漫反射材质特性 |
| `Rendering/Materials/MaterialTerrainDisplacementFeature.cs` | 位移材质特性 |

## 流式加载

| 文件 | 职责 |
|------|------|
| `Streaming/TerrainStreaming.cs` | 地形块流式加载管理 |
| `Streaming/PageBufferAllocator.cs` | 页面缓冲区分配器 |

## 着色器

| 文件 | 职责 |
|------|------|
| `Effects/Build/TerrainBuildLodLookup.sdsl` | LOD 查找构建 |
| `Effects/Build/TerrainBuildLodMap.sdsl` | LOD 贴图构建 |
| `Effects/Build/TerrainBuildNeighborMask.sdsl` | 邻接遮罩构建 |
| `Effects/Material/MaterialTerrainDiffuse.sdsl` | 漫反射着色器 |
| `Effects/Material/MaterialTerrainDisplacement.sdsl` | 位移着色器 |
| `Effects/Stream/TerrainHeightStream.sdsl` | 高度流式着色器 |
| `Effects/Stream/TerrainHeightParameters.sdsl` | 高度参数着色器 |
| `Effects/Stream/TerrainMaterialStreamInitializer.sdsl` | 材质流初始化器 |
| `Effects/TerrainForwardShadingEffect.sdfx` | 前向着色效果 |

## 辅助

| 文件 | 职责 |
|------|------|
| `BasicCameraController.cs` | 基础相机控制器 |
| `GameProfiler.cs` | 性能分析器 |

---

*最后更新: 2026-04-06*
