# Editor Avalonia Migration

## Goal

将 `Terrain.Editor` 从 Hexa.NET.ImGui 全量迁移到 Avalonia，最终由 Avalonia 桌面应用承载完整编辑器 UI，并通过 `NativeControlHost` + SDL 宿主子窗口显示 Stride 3D 视口。迁移完成后旧 ImGui 渲染器、控件、面板、shader 和依赖不再参与运行。

## Requirements

- 使用 Avalonia `SimpleTheme` 作为主题基础，不使用 FluentTheme。
- UI 采用 Avalonia 原生控件和 MVVM，使用 `CommunityToolkit.Mvvm` 暴露状态和命令。
- 视口使用 `Avalonia NativeControlHost + 纯原生 HWND 子窗口 + SDL GameContextSDL` 宿主 Stride 渲染，不走共享纹理。
- GUI 布局核心规范：
  - 尽可能使用 `Grid`、`DockPanel`、`StackPanel`、`TabControl`、`ItemsControl` 等布局容器表达结构。
  - 尽可能使用 `Margin`、`Padding`、`HorizontalAlignment`、`VerticalAlignment`、`DockPanel.Dock`、`Grid.Row`、`Grid.Column`、`ColumnSpan`、`RowSpan` 定位。
  - 禁止手算像素坐标、绝对 Canvas 定位、手写控件位置或基于窗口宽高做布局数学，除非 SDL 原生视口尺寸同步、DPI 换算等技术边界确实需要。
  - 优先使用 `Auto`、`*`、`MinWidth`、`MaxWidth`、`MinHeight`、`MaxHeight`、`ColumnSpan`、`RowSpan`、居中/左对齐/右对齐完成布局内定位。
  - 固定尺寸只用于工具栏图标按钮、分隔条、最小可用宽高等稳定交互目标，并集中成资源或样式。
- Avalonia UI 必须覆盖现有 ImGui 功能：文件菜单、工具/模式切换、视口、地形打开/保存/导出、材质/Assets、Console、Climate/Rules、Inspector、参数面板、撤销/重做和项目脏标记。
- SDL 视口必须在未聚焦状态下仍可持续绘制首帧和后续帧，并在 resize 后同步更新 presenter/backbuffer。

## Acceptance Criteria

- [ ] `dotnet build Terrain.sln` 通过。
- [ ] `Terrain.Editor` 启动 Avalonia 主窗口并使用 Simple 主题。
- [ ] 代码搜索确认运行路径无 `Hexa.NET.ImGui`、`ImGui.`、`EditorUIRenderer`、`ImGuiShader` 依赖。
- [ ] 中央视口通过 SDL 宿主显示 Stride 渲染结果，首帧可见且不因失焦停绘。
- [ ] 窗口 resize、DPI 缩放、最小化/恢复后，SDL 宿主窗口与 Stride presenter/backbuffer 尺寸保持一致，渲染稳定。
- [ ] 现有编辑器核心工作流在 Avalonia 中功能等价。
- [ ] UI XAML 不使用绝对坐标布局；除 SDL 宿主尺寸同步外，不出现手算控件位置的布局代码。

## Definition of Done

- Tests or focused verification added/updated where feasible.
- Build green.
- Editor spec 更新为 Avalonia/Simple/MVVM/SDL 宿主约定。
- 旧 ImGui 代码和依赖清理完成。
- SDL 视口失败路径有可见错误状态和诊断日志。

## Technical Approach

- 第一阶段建立 Avalonia Shell、Simple 主题资源、ViewModel 基础和 SDL 视口宿主控件。
- 第二阶段将现有服务状态适配为 ViewModel，逐步迁移每个面板功能。
- 第三阶段固定 SDL 宿主为唯一视口路线，补齐首帧出图、失焦持续渲染、resize/presenter 同步和诊断日志，再删除 ImGui 渲染器、旧 UI 树、ImGui shader 和依赖。

## Decision (ADR-lite)

**Context**: 当前编辑器 UI 深度依赖 ImGui，每帧由 `EditorGame` 渲染，视口通过 Stride render target 显示在 ImGui image 中。最初曾尝试共享纹理路线，但实现和同步复杂度过高；当前用户已明确改走 SDL 宿主。

**Decision**: 使用 Avalonia SimpleTheme + MVVM 重建 UI；Stride 继续作为渲染后端，视口通过 `NativeControlHost` 承载纯原生 `HWND` 子窗口，再由 SDL 自建 `GameFormSDL` 窗口并通过 Win32 `SetParent`/`SetWindowPos` 重挂接到该子窗口中，最终交给 `GameContextSDL` 驱动渲染。

**Consequences**: UI 层仍然保持 Avalonia 原生结构；视口嵌入复杂度从共享纹理同步转为 SDL 宿主生命周期、消息泵、DPI/resize、交换链尺寸同步、窗口重挂接和输入转发，是当前任务最高风险点。

## Out of Scope

- 不做共享纹理导入 Avalonia Composition 的实现；当前目标为 Windows x64 / D3D11 + SDL。
- 不做跨平台视口宿主抽象；当前目标为 Windows 下 SDL 宿主可用。
- 不保留 ImGui/Avalonia 双运行模式作为长期方案。

## Technical Notes

- `Directory.Packages.props` 已有 Avalonia 和 CommunityToolkit 版本，需要补充 `Avalonia.Themes.Simple`。
- Stride 桌面 SDL 视口当前采用 `GameFormSDL` 自建窗口后再 `SetParent` 到 Avalonia 原生子窗口，避免 `CreateWindowFrom(existing HWND)` 路线出现 `ClientBounds=1x1` 和黑屏。
- SDL 宿主需要处理 Avalonia 逻辑尺寸到物理像素的换算，以及 `Resize` 后交换链/backbuffer 的同步。
- `GameBase` 默认会把“未聚焦”视为近似最小化状态，嵌入式 viewport 需要显式关闭这种停绘行为，否则首帧和后续帧都可能保持黑屏。
