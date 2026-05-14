# ADR-013: Vic3 风格路径渲染

**Date**: 2026-05-12
**Status**: ✅ Accepted
**Decision ID**: ADR-013

---

## Context

路径系统（道路/河流）的渲染方式选择：写回高度图塑形 vs 贴图+alpha+深度偏移的渲染层方案。

## Decision

采用 Vic3（Victoria 3）风格的纹理化材质渲染：

- 道路：Diffuse + Normal + Properties 三通道贴图，UV 横截面 0→1 + 边缘 smoothstep 淡出
- 河流：深度剖面着色 + 正弦流动动画 + 边缘淡出
- DepthBias = -2000 / SlopeScaleDepthBias = -10 解决 z-fighting
- 道路样式用专用枚举（Dirt/Paved），不复用 MaterialSlotIndex
- 首版只做道路（土路 + 铺砌路），河流后续补齐

## Options Considered

### Option 1: 高度图写回塑形
在 PathFeatureService 中直接修改地形高度图，让路径与地形融合。

**Pros:** 路径与地形完全融合
**Cons:** 破坏性编辑，难以撤销；高度图修改影响周围区域

### Option 2: 贴图+alpha+深度偏移（选中）
路径作为独立渲染层，通过 DepthBias 浮在地形上方。

**Pros:** 非破坏性，可随时修改路径；实现简单
**Cons:** 深度偏移可能导致远处穿透

### Option 3: 混合方案
高度图写回 + 渲染层叠加。

**Pros:** 兼顾两者优点
**Cons:** 实现复杂度最高

## Rationale

参考 Victoria 3 的 Jomini Spline 框架和道路渲染系统。DepthBias 方案在 Vic3 中已被大规模验证，道路和河流都使用 alpha blend + depth bias 的方式浮在地形上方。路径编辑器已经实现了 Catmull-Rom 样条和节点编辑，只需要切换渲染方式。

## Trade-offs

**What we gain:**
- 非破坏性编辑（路径可随时修改）
- 成熟的渲染方案（Vic3 验证）
- 道路样式灵活（枚举控制，不同纹理集）

**What we give up:**
- 路径不会真正改变地形形状
- 深度偏移可能导致远处穿透（可用 SlopeScaleDepthBias 缓解）

## Implementation Notes

- `PathRoadSurface.sdsl` — 道路着色器（Diffuse/Normal/Properties + 边缘淡出 + 末端淡出）
- `PathRiverSurface.sdsl` — 河流着色器（深度剖面 + 流动动画 + 边缘淡出）
- `PathDepthBiasPipelineProcessor` — 全局 DepthBias = -2000, SlopeScaleDepthBias = -10
- `MaterialPathRoadFeature` / `MaterialPathRiverFeature` — Stride MaterialFeatureMixin
- `PathFeatureService.cs` ~2000 行，包含样条、网格生成、节点编辑、撤销重做

## References

- [开发时间线](../development-timeline.md)
- [Vic3 道路河流渲染研究](../learnings/vic3-road-river-rendering.md)
- [道路实施笔记](../learnings/road-implementation-notes.md)
- [Godot MTerrain 地形变形](../learnings/godot-mterrain-terrain-deform.md)

---

*ADR Version: 1.0*