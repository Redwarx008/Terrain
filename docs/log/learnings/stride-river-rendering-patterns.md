# Stride 河流渲染分层与标准变换链

**Topic**: River rendering subsystem pattern for Stride
**Date**: 2026-06-06
**Related Sessions**: [2026-06-06 河流渲染架构落地与顶点变换修复](../2026/06/06/2026-06-06-1-river-rendering-architecture-and-transform-fix.md), [ADR-014 河流渲染架构](../decisions/adr-014-river-rendering-architecture.md)

---

## Problem / Context

- 河流 mesh 已经能在 editor 侧生成，但正式渲染一度卡在 service-only / 临时预览路径。
- 河流还出现过屏幕空间闪烁和全屏大三角，说明 draw 虽然在跑，但 shader 没有遵守 Stride 标准位置流约定。
- 这类对象同时具备动态 mesh、独立 GPU buffer 生命周期、多 pass 和调试模式需求，不能按普通材质对象处理。

---

## Solution / Pattern

把河流作为独立渲染子系统接入 Stride：

```csharp
RiverRenderingService -> RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature
```

并在 shader 侧回到 Stride 标准 transformation 链：

```csharp
shader RiverSurface : ShaderBase, TransformationWAndVP, RiverVertexStreams, RiverWaterCommon
```

---

## Key Insights

### 1. editor façade 与正式渲染链要分开
- `RiverRenderingService` 适合承担编辑器桥接职责：接收 `RiverSegment`、驱动 mesh 生成、同步显隐。
- 正式 draw 生命周期应放到 `RiverComponent / RiverProcessor / RiverRenderObject / RiverRenderFeature`。

### 2. 多 pass 水体更适合独立 RenderFeature
- 河底 pass、水面 pass、折射中间缓冲和调试 rasterizer 状态属于 render pipeline 责任，不应塞回 service。
- 只有走 render stage / visibility group 正式链路，后续扩展才清晰。

### 3. 位置流问题优先回到 Stride 标准 mixin
- 当 shader 依赖 `PositionWS / PositionH / DepthVS` 时，优先使用 `TransformationWAndVP`。
- 如果 draw 在跑但画面表现为屏幕空间错乱，首先怀疑的是变换链缺失，而不是材质参数。

---

## When to Use

- 需要为动态生成 mesh 建立独立 GPU buffer 生命周期。
- 需要多 pass、自定义 render stage 或中间缓冲。
- 需要 editor/runtime 桥接，但又不希望编辑器 service 直接承担正式渲染逻辑。

---

## When NOT to Use

- 只是普通静态模型，且无需独立 render stage / 多 pass。
- 只是一次性预览对象，没有长期维护的渲染生命周期需求。

---

## Common Mistakes

### ❌ Mistake 1: 继续停留在 service-only 渲染
**What to avoid:**
- 在 `RiverRenderingService` 里直接管理 GPU buffer、渲染 pass 和 draw。

**Why it's bad:**
- 编辑器逻辑和渲染生命周期耦合，后续扩展多 pass、调试模式时边界会越来越乱。

**Correct approach:**
- 保留 service 作为 façade，把正式渲染链下放到 `Component -> Processor -> RenderObject -> RenderFeature`。

### ❌ Mistake 2: 用输入布局补丁替代 shader 标准变换链
**What to avoid:**
- 在 C# 输入布局侧加入 `POSITION -> POSITION_WS` 特殊映射，或按 reflection 动态拼输入布局来兜底。

**Why it's bad:**
- 这只能掩盖问题，不能替代 shader 中缺失的标准 world/view/projection 变换。

**Correct approach:**
- 让 shader 混入 `TransformationWAndVP`，回到 Stride 标准位置流输出。

### ❌ Mistake 3: 把排障代码留在正式渲染路径里
**What to avoid:**
- 在 `RiverRenderFeature` 长期保留日志、反射分支和临时输入语义映射。

**Why it's bad:**
- 会制造额外噪音，掩盖正式实现的真实边界。

**Correct approach:**
- 根因确认后，恢复固定 `RiverVertex.Layout.CreateInputElements()` 路径。

---

## Code Examples

### ✅ Good Example
```csharp
pipelineState.State.InputElements = RiverInputElements;
effect.Parameters.Set(TransformationKeys.World, riverObject.World);
effect.Parameters.Set(TransformationKeys.WorldViewProjection, riverObject.World * renderView.ViewProjection);
```

### ❌ Bad Example
```csharp
if (inputAttribute.SemanticName == "POSITION_WS")
{
    positionElement.SemanticName = "POSITION_WS";
    inputElements.Add(positionElement);
}
```

---

## Performance Considerations

- `RiverComponent` 通过 snapshot + `Version` 同步，避免每帧无差别重建 GPU 资源。
- 多 pass 渲染会增加一次额外河底绘制与中间缓冲开销，因此更需要清晰的 render object 生命周期来控制重建频率。

---

## Related Patterns

- [ADR-014 河流渲染架构](../decisions/adr-014-river-rendering-architecture.md)
- [vic3-road-river-rendering](vic3-road-river-rendering.md)

---

## References

- [2026-06-06 河流渲染架构落地与顶点变换修复](../2026/06/06/2026-06-06-1-river-rendering-architecture-and-transform-fix.md)
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`

---

*Learning Document Version: 1.0*
