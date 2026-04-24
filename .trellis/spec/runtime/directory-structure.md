# Directory Structure

> How runtime code is organized in this project.

---

## Overview

这是一个 Stride 游戏引擎地形渲染系统。项目结构分为核心库和编辑器两部分：

- **Terrain/** - 运行时核心库，包含地形组件、渲染、流送系统
- **Terrain.Editor/** - 编辑器 UI、服务、工具
- **Terrain.Windows/** - Windows 入口点
- **Shared/** - 跨项目共享代码

---

## Directory Layout

```
Terrain/
├── Assets/              # Stride 资源文件 (.sdpkg, .sdfx)
├── Core/                # 核心地形组件 (TerrainComponent)
├── Effects/             # SDSL 着色器和 SDFX 着色器效果
│   ├── Build/           # 构建阶段着色器 (LOD 计算等)
│   ├── Material/        # 材质着色器
│   └── Stream/          # 流送阶段着色器
├── Materials/           # 材质相关 C# 代码
├── Rendering/           # 渲染器和 QuadTree
├── Resources/           # 内置资源 (纹理等)
├── Streaming/           # 流送系统
├── Utilities/           # 工具类
└── *.cs                 # 入口点文件

Terrain.Editor/
├── Effects/             # 编辑器专用着色器
├── Input/                # 输入控制器
├── Models/               # 编辑器数据模型
├── Platform/            # 平台相关代码 (窗口、对话框)
├── Rendering/            # 编辑器渲染器
├── Services/             # 编辑器服务 (编辑工具、命令)
│   ├── Commands/         # 命令模式实现
│   └── Export/           # 导出功能
├── UI/                   # ImGui UI 实现
│   ├── Controls/         # UI 控件
│   ├── Dialogs/          # 对话框
│   ├── Layout/           # 布局管理
│   ├── Panels/           # 面板
│   └── Styling/          # 样式系统
└── *.cs                 # 入口点文件

Terrain.Windows/
├── Resources/            # Windows 专用资源
└── *.cs                 # 入口点文件

Shared/
└── *.cs                 # 跨项目共享代码
```

---

## Module Organization

### 命名空间规范

- 核心库: `namespace Terrain;`
- 编辑器: `namespace Terrain.Editor;`
- 子模块按目录组织，如 `Terrain.Editor.Services`, `Terrain.Editor.UI.Panels`

### 类组织原则

1. **组件类** - 继承 `Stride.Engine.ActivableEntityComponent`，放在 `Core/` 目录
2. **渲染器** - 实现 `Stride.Rendering.*` 接口，放在 `Rendering/` 目录
3. **服务类** - 负责特定功能，放在 `Services/` 目录
4. **UI 控件** - 继承自定义基类 `PanelBase` 或 `ControlBase`，放在 `UI/Controls/` 或 `UI/Panels/`

### 文件命名

- C# 类: `PascalCase.cs` (如 `TerrainComponent.cs`)
- 着色器: `PascalCase.sdsl` (如 `TerrainHeightStream.sdsl`)
- 着色器效果: `PascalCase.sdfx.cs` (如 `TerrainForwardShadingEffect.sdfx.cs`)

---

## Naming Conventions

### C# 类型

| 类型 | 命名规则 | 示例 |
|------|----------|------|
| 类 | PascalCase | `TerrainComponent`, `ClimateEditor` |
| 接口 | IPascalCase | - |
| 结构体 | PascalCase | `TerrainConfig` |
| 枚举 | PascalCase | `EditorMode`, `HeightTool` |
| 枚举值 | PascalCase | `EditorMode.Sculpt` |
| 字段 | _camelCase | `_terrainManager`, `_viewportSize` |
| 属性 | PascalCase | `CurrentMode`, `TerrainDataPath` |
| 方法 | PascalCase | `ApplyStroke()`, `UpdateRenderTarget()` |
| 事件 | PascalCase | `ToolSelected`, `HeightmapLoaded` |

### 特殊字段前缀

- 内部字段: `_` 前缀 (私有字段)
- 静态只读: `_` 前缀
- 属性 backing field: 不使用 `m_` 前缀，直接 `_fieldName`

### 着色器命名

- Stream 变量: `PascalCase` (如 `TerrainSliceIndex`)
- Shader 文件: `PascalCase.sdsl`
- Shader 内部 stage: 小写 (如 `stage stream`)

---

## Examples

### 核心组件结构

[TerrainComponent.cs](Terrain/Core/TerrainComponent.cs) - 地形组件，展示属性定义和序列化

### 编辑器服务结构

[ClimateEditor.cs](Terrain.Editor/Services/ClimateEditor.cs) - 单例服务模式

### UI 面板结构

[ToolsPanel.cs](Terrain.Editor/UI/Panels/ToolsPanel.cs) - ImGui 面板实现

### 着色器结构

[TerrainHeightStream.sdsl](Terrain/Effects/Stream/TerrainHeightStream.sdsl) - Stream 着色器定义

---

## Anti-patterns (避免这样做)

1. **不要**在核心库中引用编辑器命名空间
2. **不要**在渲染循环中创建 GC 对象
3. **不要**使用裸 `new Texture()`，使用 Stride 的资源加载系统
4. **不要**在 `Terrain/` 中添加编辑器特定的渲染特征