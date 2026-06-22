# River Render Parameter Binding Frequency
**Date**: 2026-06-22
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Reduce CPU time in `Terrain.Rendering.River.RiverRenderFeature.DrawPass` without changing the river render object granularity, without merging river meshes, and without migrating to `RootEffectRenderFeature`.

---

## Context & Background

Profiler showed `RiverRenderFeature.DrawPass` spending a large amount of CPU time setting shader parameters every frame. The user explicitly rejected mesh merging as the first fix and asked to keep the current `RootRenderFeature` path while splitting bindings by update frequency.

Relevant architecture:
- `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature`
- Existing river pass chain remains `scene seed -> bottom/refraction -> surface`

---

## What We Did

### 1. Split River Parameter Binding By Update Frequency

**Files Changed:**
- `Terrain/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- Static river textures and static bottom constants now bind during `InitializeCore` via `BindStaticRiverResources`.
- Per-view parameters (`ViewProjection`, `CameraKeys.ViewSize`) now bind once per effect per draw call before pass execution.
- Surface frame parameters (`_ViewSize`, `_ViewMatrix`, `_GlobalTime`) now bind once before surface pass work instead of once per river object.
- Surface refraction texture/sampler/size now bind once before the surface pass and are no longer passed into `DrawPass`.
- `DrawPass` now only handles pipeline setup, per-object transform matrices, vertex/index buffer binding, and draw calls.
- Bottom/surface river parameters are bound during `Prepare` using the first prepared river object as the parameter source, instead of being rebound from `Draw` or for every river object in the draw loop.
- `_RefractionMaxCameraHeight` is treated as pass/global state, not an object-local bottom/surface setting. `Draw` resolves one clamp plane for the current draw range and binds the same value to scene seed, bottom, and surface.
- Removed the extra `surfaceEffect.UpdateEffect(graphicsDevice)` call after `PrepareRiverSceneLighting`; `PrepareRiverSceneLighting` still updates both effects once after light/environment binding.

**Rationale:**
- Refraction RT, sampler, view size, view matrix, and time are shared by all river render objects in a pass.
- Keeping these bindings inside the per-object loop made CPU cost scale with river segment count even when values were identical.
- This preserves all current mesh/render object boundaries and render pass order while avoiding full bottom/surface parameter rebinding inside `Draw` / `DrawPass`.
- Runtime currently has a single global `RiverComponent`; generated river render objects are chunks/segments of that component and share the non-frame river settings bound in `Prepare`.

---

## Decisions Made

### Keep `RootRenderFeature` For This Optimization

**Decision:** Do not migrate to `RootEffectRenderFeature` in this session.

**Rationale:** `RootEffectRenderFeature` can support the current functionality in principle, but migration would require a larger rewrite around effect permutations, resource groups, and bottom/surface render stage output handling. The profiler hotspot was directly addressable inside the current `RootRenderFeature` implementation.

### Do Not Merge River Meshes

**Decision:** Preserve one render object per generated river mesh/segment.

**Rationale:** The requested fix was parameter binding frequency, not draw-call batching or mesh topology changes.

### Treat Refraction Clamp Height As Pass State

**Decision:** Keep `_RefractionMaxCameraHeight` out of `ApplyBottomParameters` and `ApplySurfaceParameters`.

**Rationale:** The clamp plane must match between scene seed packing and bottom/surface unpacking. Binding it once per draw range makes the value explicit pass state and avoids accidentally treating a global shader parameter as per-object river settings.

---

## What Worked ✅

1. **Text regression tests for binding location**
   - Updated tests to assert that shared refraction binding is not passed into the per-object draw loop.
   - Updated static bottom parameter assertions to match initialization-time binding.

2. **Small scoped refactor**
   - The pass chain and shader semantics stayed unchanged.
   - No shader files or mesh generation logic changed.

---

## What Didn't Work ❌

1. **Initial mesh batching direction**
   - The user clarified that mesh merging was not desired.
   - That direction was abandoned before production code changes were made.

---

## Architecture Impact

No new architecture decision was introduced. The current river rendering architecture remains unchanged:
- Same `RootRenderFeature` base class.
- Same render object granularity.
- Same bottom/refraction/surface pass chain.

This session is a performance refactor inside the accepted ADR-014 architecture.

---

## Code Quality Notes

### Performance

- Removed full bottom/surface river setting rebinding from `Draw`; these non-frame parameters now bind in `Prepare`.
- Reduced per-object `ParameterCollection.Set` calls for shared surface refraction and per-view/per-frame state.
- Static river resources are no longer rebound every frame from `Draw`.
- The remaining shared non-frame bottom/surface settings rely on the current single-`RiverComponent` invariant. If the renderer later supports multiple independent river components with different water settings, this path should be revisited with grouping or per-object dirty binding rather than silently using the first object.
- No runtime profiler number was captured in this session; follow-up should compare `RiverRenderFeature.DrawPass` CPU time in the editor/runtime profiler.

### Testing

Verified with:
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`
- `dotnet build Terrain/Terrain.csproj --no-restore`
- `git diff --check`

All commands completed successfully with existing package advisory warnings and CRLF warnings only.

---

## Next Session

### Immediate Next Steps

1. Profile `RiverRenderFeature.DrawPass` again and compare CPU time against the previous capture.
2. If `ParameterCollection.Set` remains hot, add a small value cache or per-render-object dirty stamp for settings that are identical across river segments.
3. If `effect.Apply` or descriptor updates become the new hotspot, revisit a staged `RootEffectRenderFeature` migration for frame/view/draw cbuffer resource groups.

---

## Quick Reference

**Key implementation:**
- `Terrain/Rendering/River/RiverRenderFeature.cs`

**Regression tests:**
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Constraint to remember:**
- Do not merge river meshes as the first response to this profiler issue.
