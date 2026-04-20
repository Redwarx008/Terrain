---
doc_type: architecture
slug: ui-panels
scope: ImGui 面板体系和编辑器全局状态
summary: MainWindow + LayoutManager 管理面板布局，EditorState 单例驱动模式切换，面板按模式显示不同内容
status: current
last_reviewed: 2026-04-20
tags: [editor, ui, imgui, panels]
depends_on: [editor-services]
---

## 1. 定位与受众

本文档描述编辑器 UI 面板体系。读者是添加新面板、修改布局、或调试 UI 状态同步时需要理解面板间关系的人。

## 2. 结构与交互

```
MainWindow
    ├─ TitleBar (自定义)
    ├─ MenuBar (File/Edit/View/Help)
    │   ├─ New Project / Open / Save / Save As
    │   ├─ Export Terrain / Export Material Descriptor
    │   └─ Undo / Redo
    └─ LayoutManager
        ├─ Top:    ToolbarPanel (模式切换 + 文件操作 + Undo/Redo)
        ├─ Left:   ToolsPanel (当前模式工具选择)
        ├─ Center: SceneViewPanel (3D 视口 + 笔刷预览)
        ├─ Right:  RightPanel (模式相关属性)
        │   ├─ SculptModePanel (雕刻参数)
        │   ├─ PaintModePanel (绘制参数 + 气候规则)
        │   └─ RuleManagerPanel / ClimateManagerPanel / RuleInspectorPanel
        └─ Bottom: ConsolePanel (日志输出)
```

### 全局状态

EditorState singleton 驱动所有面板内容：
- `CurrentEditorMode` (Sculpt/Paint/Foliage) → ToolsPanel + RightPanel 内容切换
- `CurrentHeightTool` / `CurrentPaintTool` → 工具行为
- `ActiveSeason` → 气候规则季节过滤

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| EditorState | singleton | 全局 | 内存 |
| LayoutManager | class | MainWindow | 内存 |
| EditorMode enum | enum | EditorState | 内存 |
| HeightTool / PaintTool enum | enum | EditorState | 内存 |

## 4. 关键决策

- **ImGui 自定义编辑器**：比 Stride 原生编辑器更灵活，但学习曲线更陡

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| MainWindow | `Terrain.Editor/UI/MainWindow.cs:25` | 根窗口 |
| HandleExportTerrain | `Terrain.Editor/UI/MainWindow.cs:756` | 地形导出入口 |
| HandleExportMaterialDescriptor | `Terrain.Editor/UI/MainWindow.cs:784` | 材质描述符导出 |
| HandleModeChange | `Terrain.Editor/UI/MainWindow.cs:813` | 模式切换 |
| LayoutManager | `Terrain.Editor/UI/Layout/LayoutManager.cs:12` | 面板布局管理 |
| SceneViewPanel | `Terrain.Editor/UI/Panels/SceneViewPanel.cs:38` | 3D 视口 |
| UpdateEditing | `Terrain.Editor/UI/Panels/SceneViewPanel.cs:589` | 编辑逻辑入口 |
| ToolbarPanel | `Terrain.Editor/UI/Panels/ToolbarPanel.cs:10` | 工具栏 |
| RightPanel | `Terrain.Editor/UI/Panels/RightPanel.cs:17` | 属性面板 |
| AssetsPanel | `Terrain.Editor/UI/Panels/AssetsPanel.cs:19` | 材质槽位面板 |
| InputsDataPanel | `Terrain.Editor/UI/Panels/InputsDataPanel.cs:18` | 输入数据面板 |
| ConsolePanel | `Terrain.Editor/UI/Panels/ConsolePanel.cs:29` | 日志面板 |
| EditorState | `Terrain.Editor/Services/EditorState.cs:43` | 全局状态 singleton |
| EditorMode | `Terrain.Editor/Services/EditorState.cs:12` | 模式枚举 |

## 6. 已知约束 / 边界情况

- 面板布局硬编码在 LayoutManager 中，不支持用户自定义布局
- EditorState 是 singleton，多窗口场景不支持

## 7. 相关文档

- [editor-services.md](editor-services.md) — 服务层和状态管理