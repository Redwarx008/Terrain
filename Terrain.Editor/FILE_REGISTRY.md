# Terrain Editor - 文件注册表

**命名空间:** `Terrain.Editor`
**职责:** 地形编辑器应用、UI、编辑工具

---

## 入口

| 文件 | 职责 |
|------|------|
| `Program.cs` | 应用程序入口 |
| `EditorGame.cs` | 编辑器游戏类，初始化系统 |

## 服务层

| 文件 | 职责 |
|------|------|
| `Services/TerrainManager.cs` | 地形管理服务，协调编辑操作 |
| `Services/HeightEditor.cs` | 高度编辑服务，笔刷操作 |
| `Services/TerrainRaycast.cs` | 地形射线检测 |
| `Services/EditorState.cs` | 编辑器状态管理 |
| `Services/BrushParameters.cs` | 笔刷参数定义 |
| `Services/IHeightTool.cs` | 高度工具接口 |
| `Services/HeightmapLoader.cs` | 高度图加载器 |
| `Services/TerrainSplitter.cs` | 地形分割器 |
| `Services/SplitTerrainConfig.cs` | 分割配置 |

## UI 框架

| 文件 | 职责 |
|------|------|
| `UI/MainWindow.cs` | 主窗口，协调所有面板 |
| `UI/EditorUIRenderer.cs` | UI 渲染器 |
| `UI/ImGuiExtension.cs` | ImGui 扩展方法 |
| `UI/Layout/LayoutManager.cs` | 布局管理器 |

## UI 面板

| 文件 | 职责 |
|------|------|
| `UI/Panels/PanelBase.cs` | 面板基类 |
| `UI/Panels/ToolbarPanel.cs` | 工具栏面板 |
| `UI/Panels/ToolsPanel.cs` | 工具面板（笔刷选择） |
| `UI/Panels/SceneViewPanel.cs` | 场景视图面板 |
| `UI/Panels/AssetsPanel.cs` | 资源面板 |
| `UI/Panels/ConsolePanel.cs` | 控制台面板 |
| `UI/Panels/RightPanel.cs` | 右侧面板（属性） |
| `UI/Panels/GridTileRenderer.cs` | 网格瓦片渲染 |

## UI 控件

| 文件 | 职责 |
|------|------|
| `UI/Controls/ControlBase.cs` | 控件基类 |
| `UI/Controls/Button.cs` | 按钮控件 |
| `UI/Controls/CheckBox.cs` | 复选框控件 |
| `UI/Controls/Label.cs` | 标签控件 |
| `UI/Controls/NumericField.cs` | 数值输入控件 |
| `UI/Controls/TextBox.cs` | 文本框控件 |
| `UI/Controls/Slider.cs` | 滑块控件 |
| `UI/Controls/Toggle.cs` | 开关控件 |
| `UI/Controls/Separator.cs` | 分隔符控件 |

## UI 样式

| 文件 | 职责 |
|------|------|
| `UI/Styling/ColorPalette.cs` | 颜色调色板 |
| `UI/Styling/EditorStyle.cs` | 编辑器样式定义 |
| `UI/Styling/FontManager.cs` | 字体管理器 |

## 渲染

| 文件 | 职责 |
|------|------|
| `Rendering/EditorTerrainRenderFeature.cs` | 编辑器渲染特性 |
| `Rendering/EditorTerrainEntity.cs` | 编辑器地形实体 |
| `Rendering/EditorTerrainProcessor.cs` | 编辑器地形处理器 |
| `Rendering/EditorTerrainQuadTree.cs` | 编辑器四叉树 |
| `Rendering/EditorTerrainModeController.cs` | 编辑器模式控制器 |
| `Rendering/EditorGlobalLodMap.cs` | 编辑器全局 LOD 贴图 |
| `Rendering/SceneRenderTargetManager.cs` | 场景渲染目标管理 |
| `Rendering/ViewportRenderTextureSceneRenderer.cs` | 视口渲染纹理渲染器 |
| `Rendering/Materials/MaterialEditorTerrainDiffuseFeature.cs` | 编辑器漫反射特性 |
| `Rendering/Materials/MaterialEditorTerrainDisplacementFeature.cs` | 编辑器位移特性 |

## 着色器

| 文件 | 职责 |
|------|------|
| `Effects/EditorTerrainDiffuse.sdsl` | 编辑器漫反射着色器 |
| `Effects/EditorTerrainDisplacement.sdsl` | 编辑器位移着色器 |
| `Effects/EditorTerrainHeightStream.sdsl` | 编辑器高度流式着色器 |
| `Effects/EditorTerrainHeightParameters.sdsl` | 编辑器高度参数 |
| `Effects/EditorTerrainForwardShadingEffect.sdfx` | 编辑器前向着色效果 |
| `Effects/ImGuiShaderKeys.cs` | ImGui 着色器键 |

## 平台

| 文件 | 职责 |
|------|------|
| `Platform/WindowInterop.cs` | 窗口互操作 |
| `Platform/FileDialog.cs` | 文件对话框 |

## 输入

| 文件 | 职责 |
|------|------|
| `Input/HybridCameraController.cs` | 混合相机控制器 |

---

*最后更新: 2026-04-06*
