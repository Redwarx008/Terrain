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

## Open Questions

* 笔刷投影的视觉风格：使用哪种渲染方式？

## Requirements

* 在 Sculpt / Paint / Landscape 模式下，当鼠标悬停在地形上时，显示笔刷投影圆圈
* 投影圆圈应跟随地形高度（贴地渲染）
* 显示外圈（falloff 边界，线框）和内圈（100% 强度区域，半透明填充）
* 笔刷颜色随当前工具变化（使用现有 `EditorState.GetToolColor()` / `GetPaintToolColor()`）
* 右键控制相机时隐藏笔刷投影
* 不在编辑模式或没有选中工具时隐藏投影

## Acceptance Criteria

- [ ] Sculpt 模式下鼠标悬停地形时可见笔刷投影
- [ ] Paint 模式下鼠标悬停地形时可见笔刷投影
- [ ] Landscape 模式下鼠标悬停地形时可见笔刷投影
- [ ] 笔刷投影圆圈跟随地形高度
- [ ] 外圈显示 falloff 边界，内圈显示 100% 强度区域
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

## Technical Notes

* 研究文件：`research/stride-overlay-rendering.md`
* 推荐 SceneRendererBase 方式：创建自定义 `SceneRendererBase` 子类，在 `DrawCore` 中使用自定义顶点缓冲区绘制地形跟随圆圈
* 使用 `MutablePipelineState` 配置：AlphaBlend + DepthRead + CullNone + LineList/TriangleList
* 笔刷世界位置从 `EmbeddedStrideViewportGame.RaycastTerrain()` 获取
* 需要生成跟随地形的圆圈顶点（类似 ImGui 时代的 `GenerateWorldSpaceCircle`）
* Z-fighting 预防：使用 depth bias 或世界空间偏移

## Research References

* [`research/stride-overlay-rendering.md`](research/stride-overlay-rendering.md) — Stride overlay rendering 三种方案对比，推荐 SceneRendererBase 方式

## Research Notes

### 可行方案

**方案 A: SceneRendererBase + 自定义顶点缓冲区 (推荐)**

* 创建 `EditorTerrainBrushOverlayRenderer : SceneRendererBase`
* 在 `DrawCore` 中生成跟随地形的圆圈顶点（线框外圈 + 半透明填充内圈）
* 使用 `MutablePipelineState` 配置渲染管线（AlphaBlend, DepthRead, CullNone）
* 通过 `DynamicVertexBuffer` 提交顶点数据
* 添加到 compositor 的 `SceneRendererCollection`，在主场景之后渲染
* 优点：与现有 `EditorTerrainModeController` 模式一致，最大控制度，支持地形跟随
* 缺点：需要手动管理顶点缓冲区和 pipeline state

**方案 B: Entity + GeometricPrimitive.Disc**

* 创建一个 Disc mesh Entity，使用透明材质
* 通过 Transform 跟随鼠标在地形上的位置
* 优点：概念最简单
* 缺点：无法跟随地形高度起伏（Disc 是平面的），不够灵活

**方案 C: SubRenderFeature 注入**

* 在 `EditorTerrainRenderFeature` 上添加 SubRenderFeature
* 优点：与地形渲染紧密耦合
* 缺点：耦合度高，维护困难，不够独立
