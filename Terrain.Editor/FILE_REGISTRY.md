# Terrain Editor - 文件注册表

**命名空间:** `Terrain.Editor`
**职责:** 地形编辑器应用、UI、编辑工具

---

## 入口

| 文件 | 职责 |
|------|------|
| `Program.cs` | Avalonia 桌面应用入口 |
| `App.axaml` | Avalonia 应用资源、Simple 主题入口 |
| `App.axaml.cs` | Avalonia 应用初始化和主窗口创建 |

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

## Avalonia UI 框架

| 文件 | 职责 |
|------|------|
| `Views/MainWindow.axaml` | 主窗口 XAML 布局 |
| `Views/MainWindow.axaml.cs` | 主窗口生命周期处理 |
| `Views/Controls/SharedTextureViewportControl.cs` | 共享纹理视口 Avalonia 控件，当前保留作实验路径 |
| `Views/Controls/NativeStrideViewportControl.cs` | 基于纯原生 `HWND` 子窗口的 Avalonia 视口宿主控件，当前主线路径 |

## ViewModel

| 文件 | 职责 |
|------|------|
| `ViewModels/EditorShellViewModel.cs` | 主窗口绑定状态和命令，统一创建并释放当前 SDL 视口宿主 |
| `ViewModels/SharedTextureViewportViewModel.cs` | 共享纹理视口绑定状态，并通过共享 host 暴露 Scene/TerrainManager/runtime 入口，当前保留作实验路径 |
| `ViewModels/NativeStrideViewportViewModel.cs` | SDL 原生 `HWND` 视口绑定状态，转发宿主状态和视图模式 |
| `ViewModels/ToolOptionViewModel.cs` | 工具选项绑定模型 |
| `ViewModels/ConsoleEntryViewModel.cs` | 控制台条目绑定模型 |

## 模型

| 文件 | 职责 |
|------|------|
| `Models/EditorMode.cs` | 编辑器模式和视图模式枚举 |
| `Models/TerrainFileFormat.cs` | 地形文件格式模型 |

## UI 样式

| 文件 | 职责 |
|------|------|
| `Styles/EditorTheme.axaml` | Simple 主题覆盖和编辑器资源 |

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
| `Rendering/SharedTexture/ISharedTextureViewportSource.cs` | 共享纹理视口源契约 |
| `Rendering/SharedTexture/IStrideOffscreenViewportRenderer.cs` | 离屏共享纹理帧渲染器契约和委托适配器 |
| `Rendering/SharedTexture/SharedTextureFrame.cs` | 共享纹理帧描述 |
| `Rendering/SharedTexture/SharedTextureHandleTypes.cs` | 共享纹理平台 handle 类型常量 |
| `Rendering/SharedTexture/HeadlessStrideGraphicsDeviceHost.cs` | 无窗口 Stride GraphicsDevice 生命周期宿主 |
| `Rendering/SharedTexture/DiagnosticStrideOffscreenViewportRenderer.cs` | 默认诊断清屏渲染器 |
| `Rendering/SharedTexture/SharedTextureRenderTargetManager.cs` | Stride D3D11 共享 render target 管理 |
| `Rendering/SharedTexture/D3D11SharedTextureKeyedMutex.cs` | 共享纹理 keyed mutex 同步桥，协调 Stride producer 与 Avalonia compositor |
| `Rendering/SharedTexture/StrideSceneViewportRuntime.cs` | 最小 Stride scene/compositor runtime，直接把 Scene 绘制到共享纹理 render target |
| `Rendering/SharedTexture/StrideEditorViewportHost.cs` | 集中管理共享纹理 GraphicsDevice、Scene/GraphicsCompositor/TerrainManager runtime、render loop、renderer 和 viewport source attach/detach |
| `Rendering/SharedTexture/StrideSharedTextureViewportSource.cs` | Stride 共享纹理视口源和诊断状态，拼接 host runtime 文案 |
| `Rendering/SharedTexture/StrideOffscreenViewportRenderLoop.cs` | Stride 离屏共享纹理帧循环，可由集中式 host 切换当前 viewport source |
| `Rendering/NativeViewport/NativeStrideViewportHost.cs` | 基于纯原生 `HWND` + `GameContextSDL` 的视口宿主，集中管理 SDL 窗口、Game 生命周期和 Tick |
| `Rendering/NativeViewport/EmbeddedStrideViewportGame.cs` | SDL 嵌入视口使用的最小 Stride `Game` 运行时，负责 Scene/Compositor/TerrainManager 初始化 |
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

## 输入

| 文件 | 职责 |
|------|------|
| `Input/HybridCameraController.cs` | 混合相机控制器 |

---

*最后更新: 2026-04-24*
