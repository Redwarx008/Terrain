# 首版道路重做实现摘要

## 已确认的范围

- 首版只做道路，河流后续按同一框架补齐。
- 道路首版同时支持两套 Vic3 材质：土路、铺砌路。
- 不使用 Properties 贴图。
- 放弃当前依赖高度图写回的道路视觉实现，改为依靠贴图、alpha、深度偏移和贴地渲染。
- 道路样式改为道路专用枚举或资产引用，不再复用 `MaterialSlotIndex`。
- 若已有旧项目缺少新字段，读取时对道路默认回落到 Dirt。

## 现状代码落点

- `Terrain.Editor/Services/PathFeatures/PathFeatureService.cs`
  - 当前负责路径编辑、样条采样、ribbon mesh 构建、地形写回与材质创建。
  - `RebuildPathTerrainAndMeshes()` 当前会先把 `baseHeightData` 拷回可见高度，再对每条路径执行 `ApplyFeatureTerrain(...)`，最后重建 mesh。
  - `CreateMaterial(PathFeatureKind kind)` 当前只创建纯色 Stride 材质。
- `Terrain.Editor/Services/PathFeatures/PathFeatureModels.cs`
  - `PathFeatureStyle` 目前仍有 `MaterialSlotIndex`。
- `Terrain.Editor/Services/PathFeatures/PathFeatureParameters.cs`
  - Path 工具参数当前仍暴露 `MaterialSlotIndex`。
- `Terrain.Editor/ViewModels/PathFeatureParametersViewModel.cs`
  - 绑定当前路径参数 UI。
- `Terrain.Editor/Views/MainWindow.axaml`
  - Path 模式 Inspector 当前在 `PATH SETTINGS` 里仍是 `Material` 滑条。
- `Terrain.Editor/Services/TerrainManager.cs`
  - `SavePathFeatureConfigs()` / `RestorePathData(...)` 当前保存和恢复 `MaterialSlotIndex`。
- `Terrain.Editor/Services/TomlProjectConfig.cs`
  - `TomlPathFeatureConfig` 当前仍持有 `MaterialSlotIndex`，并负责 `path_features` 读写。
- `Terrain.Editor/Rendering/PathDepthBiasPipelineProcessor.cs`
  - 当前对路径 render group 直接设置 `DepthStencilStates.None`。

## 纹理资产来源

Vic3 道路贴图实际位于：

- `E:\Victoria.3.v1.2.7\game\game\gfx\map\spline_network\road_dirt_diffuse.dds`
- `E:\Victoria.3.v1.2.7\game\game\gfx\map\spline_network\road_dirt_normal.dds`
- `E:\Victoria.3.v1.2.7\game\game\gfx\map\spline_network\roadpaved_diffuse.dds`
- `E:\Victoria.3.v1.2.7\game\game\gfx\map\spline_network\roadpaved_normal.dds`

首版应把这些资源复制到仓库内可控路径，再从项目内路径加载，不要继续依赖外部游戏目录。

## 渲染参考（来自归档研究）

参考：`../archive/2026-05/05-06-editor-road-river-drawing/research/vic3-road-river-rendering.md`

对道路首版最关键的点：

1. Diffuse + Normal 是视觉基础；Properties 不是本任务必需项。
2. 需要 alpha 淡出与适当深度偏移，避免 z-fighting。
3. 当前 ribbon UV 方向（横向 0..1 + 纵向累计距离）与研究结论兼容，可优先复用。
4. Vic3 主要是贴地渲染，不依赖再次修改地形高度去制造可见道路。

## 首版实现建议

1. 引入道路专用样式枚举，例如 `Dirt` / `Paved`。
2. 用该样式替换 `PathFeatureStyle.MaterialSlotIndex`、Path 参数单例、ViewModel、TOML 持久化字段和 Inspector UI。
3. 停止道路在 `RebuildPathTerrainAndMeshes()` 主路径里写回高度图；保留 mesh/gizmo/保存加载/撤销重做闭环。
4. 给道路构建纹理材质：加载项目内复制的 Diffuse / Normal，并替换当前纯色 `CreateMaterial` 路径。
5. 让道路透明度、贴图与深度状态尽量贴近研究结果；在不引入 Properties 的前提下，优先把视觉与贴地稳定性跑通。
6. 保持 river 代码可编译，但本次不重做河流。
