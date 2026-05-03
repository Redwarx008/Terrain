# 修复ToolBar View按钮hover样式不一致

## Goal

统一 toolbar 行中 "View" 下拉菜单项的 hover 背景色，使其与其余 toolbar 按钮（File, Edit, Select, Tools, Help）保持一致。

## What I already know

- "View" 使用 `Menu` 控件 + `toolbarMenu` class，其 MenuItem hover 背景为 `EditorSurfaceHoverBrush`（#F0F0F0）
- 其他按钮使用 `Button` + `commandButton` class，在 `Border.toolbarRow` 内 hover 背景为 #E4E2E1
- 两者颜色不一致：View 的 hover 更浅更冷，其他按钮的 hover 更深更暖

## Requirements

- 将 `Menu.toolbarMenu > MenuItem:pointerover` 的背景色改为 #E4E2E1，与 toolbar 行中其他按钮的 hover 样式一致

## Acceptance Criteria

- [ ] View 菜单项 hover 背景色与 File/Edit/Select/Tools/Help 按钮完全一致

## Definition of Done

- 修改 EditorTheme.axaml 中的样式定义
- 视觉验证一致

## Out of Scope

- toolbar 按钮的其他交互状态（pressed 等）
- 其他控件的样式调整

## Technical Notes

- 修改文件：`Terrain.Editor/Styles/EditorTheme.axaml`
- 修改位置：第 913-915 行，`Menu.toolbarMenu > MenuItem:pointerover` 选择器
- 将 `Background` 从 `{DynamicResource EditorSurfaceHoverBrush}` 改为 `#E4E2E1`