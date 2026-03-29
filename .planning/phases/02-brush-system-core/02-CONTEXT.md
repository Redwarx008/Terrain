# Phase 2: Brush System Core - Context

**Gathered:** 2026-03-29
**Status:** Ready for planning

<domain>
## Phase Boundary

实现笔刷参数配置系统，让用户能够：
1. 通过 UI 滑块调整笔刷大小、强度和衰减参数
2. 选择圆形笔刷形状（其他形状留待 Phase 5）
3. 在视口中看到笔刷大小的实时预览（圆形光标指示器）

此阶段不涉及实际的地形编辑功能，仅实现参数配置和预览。

**Requirements:** BRUSH-01, BRUSH-02, BRUSH-03, BRUSH-06

</domain>

<decisions>
## Implementation Decisions

### 参数存储与管理
- **D-01:** 笔刷参数使用全局单一状态存储（BrushParameters 服务类）
- **D-02:** 所有编辑工具共享同一组笔刷参数，切换工具时保持参数不变
- **D-03:** 参数变更通过事件通知订阅者（视口预览、未来的编辑系统）

### 参数范围与默认值
- **D-04:** Size（大小）：默认 30，范围 1-200（适合精细编辑）
- **D-05:** Strength（强度）：默认 0.5，范围 0-1，线性映射到编辑强度
- **D-06:** Falloff（衰减）：默认 0.5，范围 0-1，**反转逻辑**（1=硬边，0=软边）

### 视口预览
- **D-07:** 鼠标在视口中移动时显示圆形轮廓，表示笔刷大小
- **D-08:** 圆形轮廓使用虚线或半透明填充区分硬边区域和衰减区域
- **D-09:** 仅在视口区域有焦点时显示预览

### UI 调整
- **D-10:** 修改现有 RightPanel.BrushParamsPanel 的默认值和范围
- **D-11:** 反转 Falloff 滑块的视觉逻辑（向右=更硬，向左=更软）
- **D-12:** 初始时只启用 Circle 笔刷（其他形状显示但禁用，留待 Phase 5）

### Claude's Discretion
- 笔刷预览圆形的具体渲染样式（虚线/填充/颜色）
- 预览圆形是否跟随地形高度起伏

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### 现有 UI 组件
- `Terrain.Editor/UI/Panels/RightPanel.cs` — 笔刷参数面板和笔刷选择面板的现有实现
- `Terrain.Editor/UI/Controls/Slider.cs` — 滑块控件
- `Terrain.Editor/UI/Controls/NumericField.cs` — 数值输入控件
- `Terrain.Editor/UI/Panels/SceneViewPanel.cs` — 视口面板，需要添加笔刷预览

### 现有渲染系统
- `Terrain/Rendering/TerrainRenderFeature.cs` — 主渲染 Feature
- `Terrain.Editor/Rendering/SceneRenderTargetManager.cs` — 渲染目标管理

### 相关需求
- `.planning/REQUIREMENTS.md` — BRUSH-01, BRUSH-02, BRUSH-03, BRUSH-06

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **BrushParamsPanel**: 已有 Size、Strength、Falloff 滑块的完整 UI
- **BrushesPanel**: 已有笔刷形状选择的网格布局
- **Slider 控件**: 已支持 MinValue、MaxValue、Value、Label 等属性
- **SceneViewPanel**: 已有鼠标事件处理框架

### Established Patterns
- **事件驱动**: UI 控件使用 ValueChanged 事件通知状态变更
- **面板分离**: RightPanel 使用 Tab 组织 Params 和 Brushes
- **MVVM 风格**: 参数面板直接持有状态值

### Integration Points
- **参数存储**: 需要创建 BrushParameters 服务类，由 RightPanel 和 SceneViewPanel 共享
- **视口预览**: SceneViewPanel 需要在 RenderContent 或单独的 Overlay 层绘制笔刷预览
- **光标显示**: 需要在 SceneViewPanel 中跟踪鼠标位置并绘制圆形

### 当前 UI 默认值（需要修改）
```csharp
// RightPanel.BrushParamsPanel 当前值
BrushSize = 50.0f;      // 改为 30.0f
BrushStrength = 0.5f;   // 保持
BrushFalloff = 0.3f;    // 保持值，但反转逻辑

// 滑块范围
Size: 1.0f - 500.0f     // 改为 1.0f - 200.0f
Strength: 0.0f - 1.0f   // 保持
Falloff: 0.0f - 1.0f    // 保持范围，反转逻辑
```

</code_context>

<specifics>
## Specific Ideas

- 笔刷预览圆形可以显示内外两个圈：内圈=100%强度区域，外圈=衰减边界
- 预览颜色可以使用半透明的 Accent 色或虚线白色
- Falloff 滑块可以添加"Hard/Soft"标签提示用户方向

</specifics>

<deferred>
## Deferred Ideas

- **Square 和 Noise 笔刷形状** — Phase 5 实现
- **笔刷预设保存/加载** — 未来功能，不在 v1 范围
- **每工具独立参数** — 如果用户反馈需要可后续添加

</deferred>

---

*Phase: 02-brush-system-core*
*Context gathered: 2026-03-29*
