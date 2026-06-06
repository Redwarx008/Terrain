# 当前功能清单

**最后更新：** 2026-06-06
**状态图例：** ✅ 完成 | 🚧 进行中 | 📋 规划中 | ❌ 未开始

> **注意：** 2026-04-15 至 2026-05-14 期间有大量开发但无会话日志记录。以下功能状态基于当前代码库实际验证。

---

## 核心层 (Core)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 地形组件 (TerrainComponent) | ✅ | `Terrain/Core/TerrainComponent.cs` | [Phase 1](design/terrain-editor-design-phase-1.md) |
| 高度数据 (HeightData) | ✅ | `Terrain/Core/TerrainComponent.cs` | [Phase 1](design/terrain-editor-design-phase-1.md) |
| 流式加载 (Streaming) | ✅ | `Terrain/Streaming/TerrainStreaming.cs` | [terrain-streaming-design](../plans/terrain-streaming-design.md) |
| LOD 系统 (QuadTree) | ✅ | `Terrain/Core/` | [Phase 1](design/terrain-editor-design-phase-1.md) |

## 渲染层 (Rendering)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 地形渲染 (TerrainRenderFeature) | ✅ | `Terrain/Rendering/TerrainRenderFeature.cs` | [Phase 1](design/terrain-editor-design-phase-1.md) |
| 实例化渲染 (Instancing) | ✅ | `Terrain/Rendering/` | [instance-buffer-refactor](../plans/instance-buffer-refactor.md) |
| 材质系统 (IndexMap RGBA) | ✅ | `Terrain/Effects/Material/` | [Phase 2](design/terrain-editor-design-phase-2.md) |
| 虚拟纹理 (VT) | ✅ | `Terrain/Streaming/TerrainStreaming.cs` | [runtime-indexmap-streaming](log/2026/04/10/2026-04-10-1-runtime-indexmap-streaming.md) |
| 路径渲染 — 道路 | ✅ | `Terrain.Editor/Effects/PathRoadSurface.sdsl` | [ADR-013](log/decisions/adr-013-vic3-path-rendering.md) |
| 路径渲染 — 河流 | ✅ | `Terrain.Editor/Rendering/River/`, `Terrain.Editor/Effects/River*.sdsl` | [ADR-014](log/decisions/adr-014-river-rendering-architecture.md) |
| 路径深度偏移 | ✅ | `Terrain.Editor/Rendering/PathDepthBiasPipelineProcessor.cs` | - |

## 编辑器层 (Editor)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 高度编辑 (HeightEditor) | ✅ | `Terrain.Editor/Services/HeightEditor.cs` | [Phase 1](design/terrain-editor-design-phase-1.md) |
| 笔刷系统 (Brush System) | ✅ | `Terrain.Editor/Brushes/` | [Phase 2](design/terrain-editor-design-phase-2.md) |
| Avalonia UI | ✅ | `Terrain.Editor/Views/MainWindow.axaml` + `ViewModels/` | - |
| ~~ImGui UI~~ | ✅ 已替换 | ~~`Terrain.Editor/UI/`~~ | → 迁移至 Avalonia |
| 笔刷投影 (屏幕空间 Decal) | ✅ | `Terrain.Editor/Rendering/Decal/` | - |
| Biome 规则系统 | ✅ | `BiomeRuleService.cs`, `BiomeViewModel.cs` | - |
| Biome 蒙版绘制 | ✅ | `BiomeEditor.cs`, `BiomeMask.cs` | - |
| 纹理刷 (Texture Brush) | ✅ | `Terrain.Editor/Services/PaintEditor.cs` | [texture-brush](log/2026/04/06/2026-04-06-2-terrain-texture-brush-planning.md) |
| 纹理导入增强 | ✅ | `Terrain.Editor/Services/` | [texture-auto-normal](design/texture-auto-normal-import-and-inspector.md) |
| 统一数据同步 | ✅ | `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | - |
| 材质索引图增强 | ✅ | `Terrain.Editor/Services/MaterialIndexMap.cs` | - |
| Undo/Redo (Chunk事务) | ✅ | `Terrain.Editor/Services/Commands/` | - |
| 路径特征编辑 | ✅ | `PathFeatureService.cs`, `PathFeatureEditCommand.cs` | - |
| 河流网格生成 | ✅ | `RiverMapService.cs`, `RiverMeshService.cs` | [2026-06-05-1](log/2026/06/05/2026-06-05-1-river-mesh-generation-fix.md) |
| 河流显隐/线框调试 | ✅ | `RiverRenderingService.cs`, `RiverWireframeModeController.cs` | - |
| TOML 项目持久化 | ✅ | `Terrain.Editor/Services/ProjectManager.cs` | - |
| 导出系统 (IExporter) | ✅ | `Terrain.Editor/Services/Export/` | - |
| Biome 配置导出 | ✅ | `BiomeConfigExporter.cs` | - |
| 设置模式 (HeightScale) | ✅ | `SettingsViewModel.cs` | - |
| 资产浏览器 | ✅ | `AssetBrowserItemViewModel.cs` | - |
| 原生 SDL 视口 | ✅ | `NativeStrideViewportHost.cs` | - |
| 植被编辑 | 🚧 | - | [Phase 3](design/terrain-editor-design-phase-3.md) |

## 运行时 (Runtime)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 地形加载 | ✅ | `Terrain/Core/TerrainProcessor.cs` | - |
| 双 VT 流式加载 | ✅ | `Terrain/Streaming/TerrainStreaming.cs` | [streaming](log/2026/04/10/2026-04-10-1-runtime-indexmap-streaming.md) |
| IndexMap 材质混合 | ✅ | `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` | [streaming](log/2026/04/10/2026-04-10-1-runtime-indexmap-streaming.md) |
| RuntimeMaterialManager | ✅ | `Terrain/Materials/RuntimeMaterialManager.cs` | - |
| RuntimeBiomeConfig | ✅ | `Terrain/Materials/RuntimeBiomeConfig.cs` | - |
| Runtime DetailMap 构建 | ✅ | `Terrain/Materials/RuntimeDetailMapBuilder.cs` | - |
| 半分辨率 SplatMap | ✅ | Editor + Runtime 均支持 | - |

## 规划中 (Planned)

| 功能 | 优先级 | 设计文档 |
|------|--------|----------|
| 侵蚀模拟 | 低 | [Phase 4](design/terrain-editor-design-phase-4.md) |
| 程序化地形生成 | 低 | [Phase 4](design/terrain-editor-design-phase-4.md) |
| 笔刷预设系统 | 低 | [Phase 4](design/terrain-editor-design-phase-4.md) |
| 植被 LOD (GPU Instancing) | 中 | [Phase 5](design/terrain-editor-design-phase-5.md) |
| Compute Shader 剔除 | 中 | [Phase 5](design/terrain-editor-design-phase-5.md) |
| GPU LOD 迁移 | 中 | [Phase 7](design/terrain-editor-design-phase-7.md) |
| Hi-Z 遮挡剔除 | 低 | [Phase 7](design/terrain-editor-design-phase-7.md) |

---

## 关键架构决策摘要

| 决策 | 日期 | 备注 |
|------|------|------|
| Biome 规则层体系 | 2026-05 | [ADR-012](log/decisions/adr-012-biome-rule-layer-system.md) |
| 路径特征系统 (Road/River) | 2026-05~06 | [ADR-013](log/decisions/adr-013-vic3-path-rendering.md), [ADR-014](log/decisions/adr-014-river-rendering-architecture.md) |
| Avalonia UI 迁移 | 2026-04 | [ADR-011](log/decisions/adr-011-avalonia-sdl-viewport-hosting.md) |
| 半分辨率 SplatMap/BiomeMask | 2026-05 | 待创建 ADR |

> **注意**：2026-04-15 之前的旧 ADR 已删除（基于过时日志）。下次会话应基于当前代码状态重新创建 ADR。

---

*此文件应随功能完成状态变化而更新。详见 [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md) 获取完整架构说明。*