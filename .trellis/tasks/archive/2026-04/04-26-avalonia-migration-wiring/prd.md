# 修复 Avalonia 迁移后编辑器功能接线问题

## Goal

在不新增历史上不存在功能的前提下，恢复 `Terrain.Editor` 从 ImGui 迁移到 Avalonia 过程中断开或接错的已有编辑器能力，让 Avalonia 外壳重新正确驱动原本已经做过的项目流程、视口宿主和材质编辑流程。

## What I already know

* 现有 Avalonia 主壳体在 `Terrain.Editor/ViewModels/EditorShellViewModel.cs` 和 `Terrain.Editor/Views/MainWindow.axaml`。
* `NativeStrideViewportHost` 已存在，但 `MainWindow.axaml` 当前没有真正挂上 `NativeStrideViewportControl`。
* 迁移前已有以下历史功能：
* `65ff86a`：项目 `New/Open/Save/Save As` 通过 `TerrainManager`/`ProjectManager` 工作。
* `498fbf8`：`Export Terrain` 已实现。
* `85a785d`：`Export Material Descriptor` 已实现。
* `d96c370`：材质导入后自动选中槽位、切到 Paint 模式、自动查找 normal map。
* 当前 Avalonia 壳体里有多处占位逻辑：
* `NewProject` 把目录传给了 `ProjectManager.CreateProject(...)`，不是历史行为。
* `OpenProject` / `SaveProject` 直接绕过了 `TerrainManager.LoadProject()` / `TerrainManager.SaveProject()`。
* 导出命令只有日志，没有执行旧的 exporter。
* Paint 区域材质槽 UI 仍是静态假数据。
* 一部分工具项是占位项，不对应历史已有的后端工具。

## Assumptions (temporary)

* 本次只恢复历史提交里已经做过的能力，不为 Avalonia 新造 Water/Foliage 等尚无后端实现的编辑功能。
* 可以在现有 Avalonia 布局中把静态占位控件替换为历史能力对应的绑定和命令。

## Requirements

* Avalonia 主视口必须真正挂接 `NativeStrideViewportControl`，恢复 SDL/Stride 视口宿主。
* 项目新建、打开、保存、另存为必须沿用历史项目流转：
* `Open` 走 `TerrainManager.LoadProject(...)`
* `Save` 走 `TerrainManager.SaveProject()`
* `Save As` 保持旧逻辑：更新项目路径后，再由 `TerrainManager.SaveProject()` 写完整内容
* `Export Terrain` 和 `Export Material Descriptor` 必须重新调用现有 exporter，而不是仅打印日志。
* Paint 材质槽选择/导入流程必须恢复历史能力：
* 使用真实 `MaterialSlotManager` 数据，而不是假下拉项
* 支持导入 albedo、导入 normal、清空槽位
* 导入 albedo 后自动选中槽位，并沿用旧逻辑切到 Paint 工具
* Avalonia 工具列表只暴露已有后端支持的工具，不能继续保留明显无实现的占位工具并把它们接成“已可用”状态。

## Acceptance Criteria

* [ ] 主窗口启动后，Avalonia 视口区域实际承载 SDL/Stride 视口，而不是纯色占位框。
* [ ] `New/Open/Save/Save As` 的调用链与历史 ImGui 实现一致，不再绕开 `TerrainManager`。
* [ ] `Export Terrain` 能调用历史 `TerrainExporter` 输出 `.terrain`。
* [ ] `Export Material Descriptor` 能调用历史 `MaterialDescriptorExporter` 输出 `.toml`。
* [ ] Paint 材质槽列表来源于 `MaterialSlotManager`，导入/清空后 UI 会同步。
* [ ] 导入 albedo 后会自动选中槽位并切到 Paint 模式，符合历史 `d96c370` 行为。

## Definition of Done

* 代码可编译
* 关键历史功能已在 Avalonia 壳体重新接线
* 未新增历史上没有的编辑能力

## Out of Scope

* 新增 Water/Foliage 等尚无旧实现依据的完整编辑工作流
* 重做整个 Avalonia 视觉布局
* 为现有占位按钮凭空设计新的产品行为

## Technical Notes

* 关键历史对照：
* `65ff86a:Terrain.Editor/UI/MainWindow.cs`
* `498fbf8:Terrain.Editor/UI/MainWindow.cs`
* `85a785d:Terrain.Editor/UI/MainWindow.cs`
* `d96c370:Terrain.Editor/UI/MainWindow.cs`
* 关键当前文件：
* `Terrain.Editor/ViewModels/EditorShellViewModel.cs`
* `Terrain.Editor/Views/MainWindow.axaml`
* `Terrain.Editor/Views/Controls/NativeStrideViewportControl.cs`
* `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
* `Terrain.Editor/Services/TerrainManager.cs`
* `Terrain.Editor/Services/MaterialSlotManager.cs`
