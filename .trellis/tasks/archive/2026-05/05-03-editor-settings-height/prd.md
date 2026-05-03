# Editor Settings Mode - 地形高度设置

## Goal

在 Editor 的 mode 侧边栏中添加 Settings 模式（替换 Water mode），将地形高度(HeightScale)设置放入其中，恢复 ImGui 迁移到 Avalonia 后丢失的高度调节功能。Settings 面板布局预留分类区域以便后续扩展。

## What I already know

* EditorMode 枚举当前有：Sculpt, Paint, Foliage, Water, Landscape（Landscape 被归一化为 Paint）
* Mode 通过 `EditorShellViewModel.InitializeModes()` 注册为 `ModeOptionViewModel` 记录
* HeightScale 存在于多个层级：
  * `TomlProjectConfig.HeightScale`（默认 100.0f，持久化到 TOML）
  * `TerrainManager.HeightScale`（编辑器主控，有 `SetHeightScale()` 方法）
  * `EditorTerrainEntity.HeightScale`（每个实体，由 TerrainManager 同步）
  * `TerrainComponent.HeightScale`（运行时，默认 24.0f）
  * 着色器 cbuffer 参数 `HeightScale`
* 当前 **没有 Avalonia UI 控件**让用户修改 HeightScale，只能通过手动编辑 TOML 文件
* ImGui 时代的 UI 代码已被完全删除
* UI 模式：Slider + 只读 TextBox 的 Grid 布局
* ViewModel-Service 同步模式：双向同步 + `_syncing` 防重入

## Assumptions (temporary)

* Settings 作为 EditorMode 枚举值加入，与其他 mode 并列
* Settings mode 没有工具栏（不需要 tools）
* HeightScale 是 Settings 中的第一个设置项，后续可能添加更多设置

## Decisions

* 取消 Water mode，用 Settings mode 替代其位置
* Settings mode 图标使用 Segoe MDL2 Assets ``（齿轮）
* Settings 面板使用分类布局（如 "Terrain" 分类标题 + HeightScale），预留后续添加更多分类和设置的空间

## Requirements (evolving)

* 从 EditorMode 枚举中移除 Water
* 在 EditorMode 枚举中添加 Settings 值
* 从 InitializeModes() 中移除 Water 注册，添加 Settings 注册
* 清理 EditorShellViewModel 中 Water 相关属性（IsWaterMode 等）
* 清理 MainWindow.axaml 中 Water inspector 面板
* 在 MainWindow.axaml 的 inspector 区域添加 Settings 面板（分类布局 + HeightScale Slider + TextBox）
* 创建 SettingsViewModel 实现 HeightScale 与 TerrainManager 的双向同步
* 修改 HeightScale 后标记项目为 dirty 以触发保存

## Acceptance Criteria (evolving)

* [ ] Water mode 已从枚举、注册、属性、UI 中完全移除
* [ ] 用户可以在左侧 mode 栏选择 Settings
* [ ] 选择 Settings 后 inspector 面板显示 "Terrain" 分类 + HeightScale 滑块
* [ ] 拖动滑块实时更新 TerrainManager.HeightScale 和所有 EditorTerrainEntity
* [ ] 修改 HeightScale 后项目标记为 dirty
* [ ] HeightScale 值从项目配置加载并在保存时持久化
* [ ] Settings 面板布局有分类区域，后续可扩展

## Definition of Done

* Lint / typecheck / 构建通过
* Inspector 面板与现有 mode 面板风格一致
* ViewModel-Service 同步模式与项目中其他 ViewModel 一致

## Out of Scope (explicit)

* 运行时 TerrainComponent.HeightScale 的 UI（编辑器模式不涉及运行时组件）
* Settings 面板中除 HeightScale 之外的其他设置（后续任务）

## Technical Notes

* EditorMode 枚举：`Terrain.Editor/Models/EditorMode.cs`
* Mode 注册：`Terrain.Editor/ViewModels/EditorShellViewModel.cs` line 1032
* Mode UI：`Terrain.Editor/Views/MainWindow.axaml` line 197-221（侧边栏）
* Inspector 面板：`Terrain.Editor/Views/MainWindow.axaml` line 411-953（按 mode 条件显示）
* TerrainManager.HeightScale：`Terrain.Editor/Services/TerrainManager.cs` line 63
* TerrainManager.SetHeightScale()：line 80
* TomlProjectConfig.HeightScale：`Terrain.Editor/Services/TomlProjectConfig.cs` line 20
* BrushParametersViewModel 是双向同步模式的参考实现