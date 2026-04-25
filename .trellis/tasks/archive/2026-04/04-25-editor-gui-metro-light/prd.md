# Redesign editor GUI metro light

## Goal

将 `Terrain.Editor` 主编辑器界面重构为浅色 Windows 8 / Metro 风格的专业桌面编辑器布局，并使 Mode、Viewport、Inspector、Asset Browser 的结构与用户给定草图一致。

## Requirements

* 整体主题必须改为浅色 Windows 8 / Metro 风格，不能继续使用深色外观。
* 左侧显示 Mode 导航，默认选中 `Sculpt`。
* `Tools` 工具按钮必须集成进右侧 `Inspector`，不能在 Viewport 上方单独放一条 Tools 工具栏。
* `Inspector` 不显示显式标签页，内容由左侧 Mode 自动决定。
* Viewport 顶部保留 `View` 控件，并且必须是下拉框。
* 不显示日月切换按钮。
* 底部默认面板改为 `Asset Browser`，不再默认展示 Console。
* Sculpt Inspector 不显示 `Slope` 内容。
* Sculpt Inspector 不显示 `Biome / Climate` 内容。
* Sculpt Inspector 不显示 `Texture Layers` 内容。
* 布局需接近参考图：顶部菜单+命令区，中间左 Mode / 中 Viewport / 右 Inspector，底部 Asset Browser。

## Acceptance Criteria

* [ ] 主窗口整体为浅色 Metro 风格，使用统一资源定义颜色与控件样式。
* [ ] 左侧是 Mode 面板，默认 `Sculpt` 高亮。
* [ ] 右侧 Inspector 顶部包含当前模式下的工具按钮。
* [ ] Viewport 上方不再存在独立 Tools 工具栏。
* [ ] Viewport 顶部的 `View` 使用下拉框。
* [ ] 界面中不出现日月切换按钮。
* [ ] 底部主面板标题与内容为 `Asset Browser`。
* [ ] Sculpt 模式下 Inspector 不显示坡度、Biome/Climate、Texture Layers 相关内容。
* [ ] `Terrain.Editor` 可成功构建。

## Out of Scope

* 不实现真实资产浏览器后端数据源，只提供符合目标布局的默认 Asset Browser 界面。
* 不扩展新的地形编辑运行时能力，仅重构编辑器外壳界面与模式映射。

## Technical Notes

* 主要改动文件预计为 `Terrain.Editor/Views/MainWindow.axaml`、`Terrain.Editor/Styles/EditorTheme.axaml`、`Terrain.Editor/ViewModels/EditorShellViewModel.cs`、`Terrain.Editor/Models/EditorMode.cs`。
* 当前实现存在显式 `TabControl` Inspector、底部 `Console`、顶部模式按钮栏，需要整体改造。
