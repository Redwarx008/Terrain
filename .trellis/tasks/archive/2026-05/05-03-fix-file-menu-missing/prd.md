# 修复 Avalonia 迁移后丢失的 File 菜单项

## Goal

从 ImGui 迁移到 Avalonia 后，工具栏上的 File 按钮只是一个没有 Flyout 的空 Button，点击无反应。恢复 ImGui 时代存在的完整菜单结构。

## Requirements

* File 按钮添加 MenuFlyout：New, Open, 分隔线, Save, Save As, 分隔线, Export 子菜单(Terrain, Material Descriptor), 分隔线, Exit
* Edit 按钮添加 MenuFlyout：Undo, Redo
* Help 按钮添加 MenuFlyout：About
* 各菜单项 Command 绑定到 EditorShellViewModel 对应命令
* 快捷键通过 HotKey 属性显示并添加 Window.KeyBindings
* Select / Tools 保持原样（无 Flyout），与 ImGui 后期行为一致

## Acceptance Criteria

* [x] 点击 File 按钮弹出下拉菜单，包含所有预期项
* [x] 点击 Edit 按钮弹出下拉菜单（Undo/Redo）
* [x] 点击 Help 按钮弹出下拉菜单（About）
* [x] Export 子菜单正确展开
* [x] 各菜单项 Command 绑定正确
* [x] 菜单项旁边显示快捷键提示
* [x] Ctrl+N/O/S 键盘快捷键可用

## Definition of Done

* XAML 修改通过编译 ✅
* 手动验证各菜单可点击、弹出、执行
* 快捷键与菜单提示一致

## Technical Notes

* 修改文件：`Terrain.Editor/Views/MainWindow.axaml`
* 所有 Command 已在 `EditorShellViewModel.cs` 中存在，无需修改 ViewModel
* 使用 `Button.Flyout > MenuFlyout` 模式，与现有 View 按钮一致