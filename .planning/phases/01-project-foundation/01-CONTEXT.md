# Phase 1: Project Foundation - Context

**Gathered:** 2026-03-29
**Status:** Ready for planning

<domain>
## Phase Boundary

实现实时 3D 地形预览与相机导航系统，让用户能够：
1. 在编辑器中看到带 LOD 的地形渲染
2. 使用轨道/自由飞行混合模式导航相机
3. 通过 File → Open 加载高度图文件

此阶段不涉及地形编辑功能，仅实现预览和导航。

</domain>

<decisions>
## Implementation Decisions

### 地形数据来源
- **D-01:** 编辑器启动时显示空场景，用户通过 File → Open 导入高度图
- **D-02:** 支持从 PNG 高度图文件加载（16-bit 灰度）
- **D-03:** 加载后自动创建 Terrain 实体并添加到场景

### 相机导航模式
- **D-04:** 混合相机模式：默认轨道旋转，按住键切换自由飞行
- **D-05:** 轨道中心点可由用户调整（平移焦点），双击重置到地形中心
- **D-06:** 右键拖拽旋转、中键拖拽平移、滚轮缩放（已有基础实现）
- **D-07:** 自由飞行模式使用 WASD + 鼠标视角

### 渲染集成方式
- **D-08:** Stride 3D 渲染嵌入 ImGui 窗口内（SceneViewPanel 区域）
- **D-09:** 使用 RenderTarget 将 Stride 渲染结果绘制到 ImGui Image
- **D-10:** 需要处理 ImGui 窗口尺寸变化与 Stride BackBuffer 同步

### LOD 策略
- **D-11:** 复用现有 Terrain/Rendering/ 的完整 LOD 系统
- **D-12:** 包括 TerrainQuadTree、TerrainStreamingManager、TerrainComputeDispatcher
- **D-13:** 相机参数传递给 LOD 系统，用于 Chunk 选择和 Streaming 优先级

### Claude's Discretion
- 默认高度图尺寸（如用户未加载文件时的占位）— 可选用小的测试高度图或完全不显示地形
- 相机初始位置和视角 — 自动适配加载的地形边界

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### 核心渲染系统
- `Terrain/Rendering/TerrainRenderFeature.cs` — 主渲染 Feature，管理渲染管线
- `Terrain/Rendering/TerrainRenderObject.cs` — GPU 资源持有者
- `Terrain/Rendering/TerrainQuadTree.cs` — LOD 选择和视锥剔除
- `Terrain/Rendering/TerrainComputeDispatcher.cs` — Compute Shader 调度

### 地形组件和处理器
- `Terrain/Core/TerrainComponent.cs` — 地形组件定义
- `Terrain/Core/TerrainProcessor.cs` — 实体处理器，初始化和更新

### 流式加载
- `Terrain/Streaming/TerrainStreaming.cs` — 异步高度图加载和 GPU 上传
- `Terrain/Streaming/PageBufferAllocator.cs` — 缓冲池管理

### 编辑器已有代码
- `Terrain.Editor/EditorGame.cs` — 游戏主类，场景初始化
- `Terrain.Editor/UI/Panels/SceneViewPanel.cs` — 场景视图面板，相机输入处理
- `Terrain.Editor/UI/MainWindow.cs` — 主窗口，面板布局

### 研究文档
- `.planning/research/ARCHITECTURE.md` — 架构研究，组件边界和数据流
- `.planning/research/PITFALLS.md` — 陷阱研究，GPU-CPU 同步问题

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **TerrainRenderFeature**: 完整的 LOD 渲染管线，可直接复用
- **TerrainQuadTree**: 基于 MinMaxErrorMap 的 LOD 选择，需要相机参数
- **TerrainStreamingManager**: 异步加载高度图页面，已有 LRU 淘汰
- **SceneViewPanel**: 已有相机输入处理框架（右键旋转、中键平移、滚轮缩放）
- **EditorGame.InitializeScene()**: 已有场景、相机、灯光初始化

### Established Patterns
- **ECS 模式**: TerrainComponent + TerrainProcessor 处理地形实体
- **渲染管线**: TerrainRenderFeature 注册到 GraphicsCompositor
- **ImGui 集成**: EditorUIRenderer 在 Game.Draw() 后渲染 ImGui

### Integration Points
- **场景初始化**: EditorGame.InitializeScene() — 需要添加地形实体
- **相机传递**: SceneViewPanel.Camera → TerrainProcessor/QuadTree
- **文件菜单**: MainWindow 菜单栏 — 添加 File → Open 回调
- **渲染目标**: SceneViewPanel.SceneRenderTarget — Stride 渲染到 Texture

</code_context>

<specifics>
## Specific Ideas

- 双击 SceneViewPanel 重置相机焦点到地形中心
- 自由飞行模式切换键：可能是 Shift 或鼠标侧键（Claude 可选）
- 空场景时显示提示文本 "Use File → Open to load a heightmap"

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 01-project-foundation*
*Context gathered: 2026-03-29*
