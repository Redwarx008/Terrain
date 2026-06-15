# 当前功能清单

**最后更新：** 2026-06-15
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
| 河流网格生成 | ✅ | `RiverMapService.cs`, `RiverMeshService.cs`, `RiverViewModel.cs` | 启动或运行期加载 `rivers.png` 后会自动生成 mesh；宽度缩放仍可触发重建；不再暴露手动 Import/Generate UI；River inspector 仅保留资源路径、生成状态与宽度缩放 |
| 河流显隐/线框调试 | ✅ | `RiverRenderingService.cs`, `RiverWireframeModeController.cs` | - |
| 虚拟资源会话 | ✅ | `Terrain.Editor/Services/Resources/`, `Terrain/Resources/` | Editor/Runtime 优先扫描工作区 `game/` 作为 base；若起点本身已位于目录名为 `game` 且包含 `map_data/` 的合法根，也会直接接受该根，并从 `exe/LaunchSetting.json` 读取或自动生成本地 mod 配置；`game/` 不再由 Git 跟踪 |
| MapData 缺失骨架补齐 | ✅ | `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs` | 自动生成三个 TOML，并在文件顶部保留注释模板示例；`heightmap.png` 仍需人工补齐 |
| 作者态缺失材质降级加载 | ✅ | `Terrain.Editor/Services/Resources/EditorMaterialRecoveryService.cs`, `Terrain.Editor/Services/MaterialSlotManager.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs` | 缺失 `material_id` 时为每个缺失项创建运行时默认槽位并逐条打印错误；缺失贴图文件时按通道逐槽降级：`albedo` 回退洋红缺失材质纹理、`normal` 回退 flat normal、`properties` 仅记录诊断；仅缺贴图文件仍允许 `Save` / `Export`，缺失 `material_id` 时会阻止 `Save` / `Export` |
| `map_data` TOML 规格文档 | ✅ | `docs/design/map-data-toml-formats.md` | 记录 `default.toml`、`materials/descriptor.toml`、`biome_settings.toml` 的当前实现字段、约束、默认值与 Editor/Runtime 消费边界 |
| Save 作者态资源 | ✅ | `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs` | `Save` 通过异步模态进度写回 `default.toml` / heightmap / biome mask / biome settings / materials descriptor；UI 线程先捕获不可变 save snapshot，后台只做文件写入；保存期间禁用 Save/Export/Import/Undo/Redo 等可变更命令，并阻止 Stride 视口输入，避免大图 PNG 编码时误判为进程卡死；缺失 `biome_mask.png` 时首次保存再生成；作者态保存使用事务化写回，后续 writer 失败时会回滚前面已 staged 的资源；当前不写回 `rivers.png` 与材质贴图文件；若存在缺失 `material_id` 的运行时临时槽位则禁止保存 |
| TOML 项目持久化 | ❌ 已移除 | 旧 `ProjectManager.cs` / `TomlProjectConfig.cs` 已删除 | Editor 固定 Terrain 工作区 |
| 导出系统 (IExporter) | ✅ | `Terrain.Editor/Services/Export/` | - |
| Biome 配置导出 | ❌ 已移除 | 旧 `BiomeConfigExporter.cs` 已删除 | Runtime 改用 `map_data/biome_settings.toml` |
| 设置模式 (HeightScale) | ✅ | `SettingsViewModel.cs` | - |
| 资产浏览器 | ✅ | `AssetBrowserItemViewModel.cs` | - |
| 原生 SDL 视口 | ✅ | `NativeStrideViewportHost.cs` | - |
| 植被编辑 | 🚧 | - | [Phase 3](design/terrain-editor-design-phase-3.md) |

## 运行时 (Runtime)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 地形加载 | ✅ | `Terrain/Core/TerrainProcessor.cs`, `Terrain/Resources/GameRuntimeResourceBootstrap.cs` | Runtime 从工作区 `game/` 根定位资源并读取 `.terrain`；忽略 `default.toml` 中的 `heightmap` 声明；缺失 `.terrain` 或 `biome_mask.png` 时记错误日志并保持未初始化；同配置失败后不逐帧重试 |
| 双 VT 流式加载 | ✅ | `Terrain/Streaming/TerrainStreaming.cs` | [streaming](log/2026/04/10/2026-04-10-1-runtime-indexmap-streaming.md) |
| IndexMap 材质混合 | ✅ | `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` | [streaming](log/2026/04/10/2026-04-10-1-runtime-indexmap-streaming.md) |
| RuntimeMaterialManager | ✅ | `Terrain/Materials/RuntimeMaterialManager.cs` | descriptor 驱动 |
| 虚拟资源 Bootstrap | ✅ | `Terrain/Resources/` | `gameRoot` 扫描或 direct-hit 合法 `game/` 根 + `exe/LaunchSetting.json` + `GameResourceResolverBootstrap` + resolver/bootstrap |
| Editor 作者态写回器 | ✅ | `Terrain.Editor/Services/Resources/*Writer.cs` | 写回当前命中的 `default.toml` / heightmap / biome_mask / biome_settings / materials descriptor；rivers 当前仅可选读取，不写回 |
| Runtime DetailMap 构建 | ✅ | `Terrain/Materials/RuntimeDetailMapBuilder.cs` | 高度来源于 `.terrain` 内数据，而不是 `heightmap.png` |
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
