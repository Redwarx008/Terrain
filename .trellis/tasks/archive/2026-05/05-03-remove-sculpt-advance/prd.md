# 删除Sculpt编辑器Advance栏

## Goal

移除 Sculpt 模式 Inspector 中的 ADVANCED 区域，因为其中的三个选项均无实际功能。

## What I already know

- ADVANCED 区域位于 `Terrain.Editor/Views/MainWindow.axaml` 第 468-476 行
- 包含 3 个 CheckBox：Use Target Value、Auto-LOD、Show Brush Wireframe
- 三者均无 `{Binding}` 绑定，C# 代码中无对应属性
- 勾选/取消没有任何实际效果，纯占位 UI

## Requirements

- 删除 MainWindow.axaml 中 ADVANCED 区域的 XAML 代码（约 9 行）

## Acceptance Criteria

- [ ] Sculpt 模式 Inspector 中不再显示 ADVANCED 区域
- [ ] 编译通过，无残留引用

## Out of Scope

- 为这些功能添加真实实现（当前只需移除无效 UI）

## Technical Notes

- 文件：`Terrain.Editor/Views/MainWindow.axaml`，行 468-476
- 无需修改任何 C# 文件，无依赖代码