# 修复 Avalonia 迁移后笔刷投影不显示

## Goal

从 ImGui 迁移到 Avalonia 后，viewport 中不再显示笔刷在地面上的投影预览（圆圈），导致用户编辑地形时无法看到笔刷范围。需要在需要笔刷的模式下恢复笔刷投影显示。

## What I already know

* ImGui 时代的 `SceneViewPanel.cs` 使用 `ImDrawList` 在屏幕空间绘制了跟随地形的笔刷圆圈预览，包括外圈（falloff 边界）和内圈（100% 强度区域）
* 迁移到 Avalonia 后所有 ImGui 代码被删除，笔刷预览渲染代码未被重新实现
* 当前 `EmbeddedStrideViewportGame.UpdateBrush()` 已有射线检测获取笔刷世界坐标的逻辑
* `BrushParameters` 单例提供 Size/Strength/Falloff 参数
* `EditorState.GetToolColor()` / `GetPaintToolColor()` 已定义了工具颜色
* viewport 使用 SDL 原生子窗口嵌入 Avalonia，Avalonia 无法直接绘制 Stride backbuffer
* 项目已有 `EditorTerrainModeController` 动态添加渲染阶段的模式
* `PresenterViewportSceneRenderer` 包装了 `SceneRendererCollection`

## Assumptions (temporary)

* 笔刷投影只需要在地形编辑相关模式（Sculpt、Paint、Landscape）下显示
* 笔刷投影应跟随地形高度（贴地），而非纯屏幕空间2D圆圈
* 不需要在 Foliage/Water 模式下显示（这些模式目前没有笔刷功能）

## Decision

* 笔刷投影采用**屏幕空间贴花 (Screen-Space Decal)** 方式实现
* 参考实现：`XenkoProofOfConcepts/ScreenSpaceDecalRootRendererExample` (Basewq)
* 走完整的 `RootRenderFeature + Component → Processor → RenderObject` 链，与 Stride 渲染管线正确集成

## Requirements

* 在 Sculpt / Paint / Landscape 模式下，当鼠标悬停在地形上时，显示笔刷投影圆圈
* 投影圆圈应跟随地形高度（贴地渲染）
* 投影应表现为单一圆形笔刷遮罩，并通过 falloff 呈现从中心到边缘的透明度过渡
* 笔刷颜色随当前工具变化（使用现有 `EditorState.GetToolColor()` / `GetPaintToolColor()`）
* 右键控制相机时隐藏笔刷投影
* 不在编辑模式或没有选中工具时隐藏投影

## Acceptance Criteria

- [ ] Sculpt 模式下鼠标悬停地形时可见笔刷投影
- [ ] Paint 模式下鼠标悬停地形时可见笔刷投影
- [ ] Landscape 模式下鼠标悬停地形时可见笔刷投影
- [ ] 笔刷投影圆圈跟随地形高度
- [ ] 笔刷投影显示单一圆形遮罩，边缘按 falloff 平滑衰减
- [ ] 笔刷颜色随工具类型变化
- [ ] 右键旋转相机时投影消失
- [ ] 无选中工具时无投影显示
- [ ] 不在编辑模式（Foliage/Water）时无投影显示

## Definition of Done

* Lint / typecheck / CI green
* 在 viewport 中实际验证笔刷投影显示正确

## Out of Scope

* Foliage / Water 模式的笔刷投影（当前这些模式没有笔刷编辑功能）
* 笔刷投影的 GPU 着色器特效（发光、动画等）
* 自定义笔刷形状（当前只支持圆形）

## Technical Approach

### 架构：屏幕空间 Decal 系统

走 `Component → Processor → RenderObject → RootRenderFeature` 完整链：

| 组件 | 职责 |
|---|---|
| `BrushDecalComponent` | EntityComponent — 存纹理、颜色、缩放、RenderGroup |
| `BrushDecalProcessor` | EntityProcessor — 每帧同步 Transform + 参数到 RenderObject |
| `BrushDecalRenderObject` | RenderObject — 持有 WorldMatrix, Texture, Color, Scale, Cube 图元 |
| `BrushDecalRootRenderFeature` | RootRenderFeature — Draw 中绑定深度 SRV + shader 参数 + 画 Cube |
| `DecalShader.sdsl` | 屏幕空间贴花着色器 — 深度重建世界位置 + clip盒内检测 + 程序化圆形笔刷遮罩 |
| `BrushDecalRenderStageSelector` | RenderStageSelector — 分配到 Transparent 阶段 |

### 着色器原理

参考 `DecalShader.sdsl`：
- VS: 计算顶点 ViewSpace 位置 (供 PS 做射线投影)
- PS: 从屏幕坐标读深度缓冲 → 重建世界位置 → 转 Cube 局部空间 → `clip()` 丢弃盒外像素 → 基于局部 XZ 生成圆形遮罩并按 falloff 衰减 alpha
- Pipeline: `AlphaBlend + DepthRead`, CullMode.Back
- 继承: `DepthBase, ShaderBase, Transformation, PositionStream4, ComputeColor`

### 集成方式

1. Compositor: 添加 `BrushDecalRootRenderFeature` 到 Render Features
2. RenderStage: 添加 `BrushDecalRenderStageSelector`, 指向 Transparent 阶段
3. Entity: 创建不可见 Entity, 挂载 `BrushDecalComponent`
4. 更新: 每帧将笔刷世界位置同步到该 Entity 的 Transform

## Research References

* [`research/stride-decal-support.md`](research/stride-decal-support.md) — Stride 引擎零内置 decal 支持的确认
* [`research/stride-overlay-rendering.md`](research/stride-overlay-rendering.md) — Stride overlay 渲染方案对比
* 参考实现: `XenkoProofOfConcepts/ScreenSpaceDecalRootRendererExample` (cloned to /tmp/XenkoProofOfConcepts)
