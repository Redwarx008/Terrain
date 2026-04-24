# Directory Structure

> How editor code is organized in this project.

---

## Overview

本项目的"前端"目标是 Avalonia 桌面界面，位于 `Terrain.Editor/` 目录。这是一个游戏引擎编辑器，使用 C# + Avalonia + MVVM 进行界面开发；旧 ImGui 目录结构只作为迁移参考。

---

## Directory Layout

```
Terrain.Editor/
├── Input/                # 输入控制器
├── Models/               # 数据模型
├── Platform/            # 平台相关 (窗口、对话框)
├── Rendering/            # 渲染器
├── Services/             # 编辑器服务 (核心逻辑)
│   ├── Commands/         # 命令模式
│   └── Export/          # 导出功能
├── ViewModels/           # Avalonia 绑定状态和命令
├── Views/                # Avalonia XAML 视图
│   ├── Controls/         # 可复用 Avalonia 控件
│   ├── Dialogs/          # Avalonia 对话框
│   └── Panels/           # 编辑器面板视图
└── Styles/               # Simple 主题覆盖、资源和控件样式
```

---

## Module Organization

### 命名空间规范

所有 Avalonia UI 代码按职责放置：
- `Terrain.Editor.Views` - XAML 视图和 code-behind
- `Terrain.Editor.Views.Panels` - 面板视图
- `Terrain.Editor.Views.Controls` - 可复用控件
- `Terrain.Editor.ViewModels` - 绑定状态、命令、服务适配
- `Terrain.Editor.Styles` - 主题资源和样式

### 类组织原则

1. **视图 (View)** - 使用 Avalonia XAML 声明布局，放置在 `Views/`
2. **ViewModel** - 使用 `ObservableObject` / `RelayCommand` 暴露状态和命令，放置在 `ViewModels/`
3. **可复用控件** - 继承 Avalonia 控件或 `UserControl`，放置在 `Views/Controls/`
4. **对话框** - 使用 Avalonia `Window` 或 `UserControl`，放置在 `Views/Dialogs/`
5. **服务** - 业务逻辑放置在 `Services/`

### 文件命名

- 视图: `*.axaml` + `*.axaml.cs` (如 `MainWindow.axaml`)
- ViewModel: `*ViewModel.cs`
- 面板视图: `*PanelView.axaml`
- 控件: `*Control.axaml` 或 `*Control.cs`
- 对话框: `*Dialog.axaml`
- 服务: `*Editor.cs` / `*Service.cs` (如 `ClimateEditor.cs`, `ProjectManager.cs`)

---

## UI Control Types

| 类型 | 基类 | 目录 | 示例 |
|------|------|------|------|
| 主窗口 | `Window` | `Views/` | `MainWindow` |
| 面板 | `UserControl` | `Views/Panels/` | `ToolsPanelView`, `AssetsPanelView` |
| 控件 | Avalonia 控件 / `UserControl` | `Views/Controls/` | `ViewportControl` |
| 对话框 | `Window` / `UserControl` | `Views/Dialogs/` | `ExportProgressDialog` |
| 状态 | `ObservableObject` | `ViewModels/` | `EditorShellViewModel` |

---

## Examples

### 面板结构

[ToolsPanel.cs](Terrain.Editor/UI/Panels/ToolsPanel.cs) - 工具面板，展示面板实现

### 控件结构

[Controls/Button.cs](Terrain.Editor/UI/Controls/Button.cs) - 按钮控件

### 服务结构

[ClimateEditor.cs](Terrain.Editor/Services/ClimateEditor.cs) - 单例服务模式

---

## Anti-patterns

1. **不要**新增 ImGui 代码
2. **不要**在 `Terrain/` 核心库中编写编辑器 UI 代码
3. **不要**在普通 UI 视图中直接操作图形设备；共享纹理视口例外，但必须隔离在专用控件/服务中
4. **不要**使用硬编码颜色值，使用 Avalonia 资源
5. **不要**用绝对坐标或手算像素位置进行布局
