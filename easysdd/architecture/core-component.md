---
doc_type: architecture
slug: core-component
scope: 运行时地形组件、Processor 和 RenderObject 的生命周期与数据管线
summary: TerrainComponent 持配置和运行时状态，TerrainProcessor 负责初始化与帧更新，TerrainRenderObject 持有全部 GPU 资源
status: current
last_reviewed: 2026-04-20
tags: [runtime, component, processor, render-object]
depends_on: [streaming, render-pipeline]
---

## 0. 术语

| 术语 | 定义 |
|---|---|
| TerrainConfig | 触发重建的配置参数结构体，IEquatable |
| TerrainChunkNode | GPU 实例数据（NodeInfo + StreamInfo + SplatInfo），每个可见 chunk 一个 |
| LoadedTerrainData | Processor 加载完成后的地形数据包 |

## 1. 定位与受众

本文档描述运行时地形系统的入口层：组件、处理器和渲染对象。读者是做 runtime streaming、LOD 或材质系统时需要理解初始化链路和 GPU 资源归属的人。

## 2. 结构与交互

```
TerrainComponent (配置 + 运行时状态)
    ↓ OnAttach
TerrainProcessor (初始化管线)
    ├─ TerrainFileReader → LoadedTerrainData
    ├─ TerrainRenderObject (GPU 资源容器)
    ├─ TerrainStreamingManager (页调度)
    ├─ RuntimeMaterialManager (材质纹理数组)
    └─ TerrainQuadTree (LOD 选择)
```

### 关键交互

- **组件→处理器**：`GenerateComponentData` 从 Component 属性创建 RenderObject
- **处理器→流式**：`ApplyLoadedTerrainData` 初始化 `TerrainStreamingManager`
- **处理器→材质**：`EnsureMaterial` / `UpdateMaterialParameters` 驱动 `RuntimeMaterialManager`
- **配置变更检测**：`TerrainConfig` 结构体比较 `LoadedConfig != currentConfig` 触发重建

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| TerrainDataPath / MaterialConfigPath | string | TerrainComponent | .terrain / .toml 文件 |
| TerrainConfig | struct | TerrainComponent | 内存（运行时） |
| TerrainChunkNode[] | struct[] | TerrainRenderObject | GPU Buffer |
| HeightmapArray / SplatMapArray | Texture | TerrainRenderObject | GPU（流式加载） |
| MinMaxErrorMap[] | class[] | TerrainComponent | 内存（从 .terrain 加载） |

## 4. 关键决策

- **TerrainConfig 结构体比较触发重建**：多个 Loaded* 字段合并为 IEquatable 结构体，一次比较替代多次字段判断 → `2026-04-20-trick-config-struct-consolidation.md`
- **.terrain 二进制格式**：预计算 MinMaxErrorMap 避免运行时重算 → `streaming.md`

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| TerrainComponent | `Terrain/Core/TerrainComponent.cs:20` | 主组件类 |
| TerrainConfig struct | `Terrain/Core/TerrainComponent.cs:117` | 配置变更检测结构体 |
| TerrainProcessor | `Terrain/Core/TerrainProcessor.cs:19` | 实体处理器 |
| GenerateComponentData | `Terrain/Core/TerrainProcessor.cs:28` | 创建 RenderObject |
| ApplyLoadedTerrainData | `Terrain/Core/TerrainProcessor.cs:167` | 完整初始化管线 |
| TerrainRenderObject | `Terrain/Rendering/TerrainRenderObject.cs:13` | GPU 资源容器 |
| ReinitializeGpuResources | `Terrain/Rendering/TerrainRenderObject.cs:31` | GPU 资源重建 |
| CreatePatchGeometry | `Terrain/Rendering/TerrainRenderObject.cs:84` | Patch 几何体创建 |
| LoadedTerrainData | `Terrain/Core/TerrainProcessor.cs:439` | 加载数据包 record struct |

## 6. 已知约束 / 边界情况

- TerrainConfig 比较失败时整个 RenderObject 重建，不支持部分热更新
- MaxResidentChunks 默认 1024，超出时 LRU 淘汰
- HeightmapArray 最多绑定 8 个 slice（编辑器 sliced heightmap 限制）

## 7. 相关文档

- [streaming.md](streaming.md) — 流式加载和虚拟纹理
- [render-pipeline.md](render-pipeline.md) — 渲染管线和 LOD 选择
- `2026-04-20-trick-config-struct-consolidation.md` — TerrainConfig 合并技巧