# Custom Forward Renderer Task 2
**Date**: 2026-06-24
**Session**: Ocean CK3 core water Task 2
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

Add the `Terrain.Rendering.CustomForwardRenderer` framework and register it in the runtime graphics compositor, without implementing the shared water refraction capture or migrating Ocean/River internals.

---

## Context & Background

Task 1 had already added failing `WaterRenderingTextTests`. This session implemented Task 2 from `docs/superpowers/plans/2026-06-24-ocean-ck3-core-water.md`, but followed the project rule that superpowers process artifacts are local workflow aids and should not be committed by default.

Reference renderer:
- `E:\WorkSpace\StrideStreamingTerrain\StrideTerrain\Rendering\CustomForwardRenderer.cs`

---

## What We Did

### 1. Added CustomForwardRenderer framework
**Files Changed:** `Terrain/Rendering/CustomForwardRenderer.cs`

- Created `public partial class CustomForwardRenderer : SceneRendererBase, ISharedRenderer` in namespace `Terrain.Rendering`.
- Preserved the reference forward renderer structure:
  - shadow map drawing
  - opaque and transparent stage collection/draw
  - optional GBuffer/z-prepass
  - depth-as-SRV and opaque-as-SRV binding for transparent rendering
  - render target/depth preparation
  - post effects
  - VR per-view constant buffer helper
- Added feature lookup for `OceanRenderFeature` and `RiverRenderFeature` in `InitializeCore`.
- Added placeholder `waterRefractionCapturePass` field as `object?` because `WaterRefractionCapturePass` is intentionally not created until Task 3.
- Added no-op hooks:
  - `DrawWaterRefractionCapture`
  - `DrawOceanWater`
  - `DrawRiverWaterChain`
- Called those hooks after opaque draw and before generic transparent draw. They are no-op in this slice, so existing Ocean/River transparent rendering still happens through the current selectors.

### 2. Registered renderer in runtime compositor
**Files Changed:** `Terrain/Assets/GraphicsCompositor.sdgfxcomp`

- Changed the Game/Editor shared renderer object and references from Stride `ForwardRenderer` to `!Terrain.Rendering.CustomForwardRenderer,Terrain`.
- Left `SingleView` as the original Stride `ForwardRenderer`.
- Left Ocean/River transparent selectors in place because double-draw prevention belongs to later tasks.

### 3. Kept editor tone mapping compatible
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

- Updated `FindPostProcessingEffects` to recognize `CustomForwardRenderer { PostEffects: PostProcessingEffects postEffects }`.
- Kept the existing Stride `ForwardRenderer` pattern for fallback and `SingleView` compatibility.

### 4. Updated status documentation
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

- Documented that `CustomForwardRenderer` is only a framework/hook layer at this point.
- Explicitly noted that shared refraction capture and renderer-driven Ocean/River draw APIs remain future work.

---

## Problems Encountered & Solutions

### Stride CommandList API differed from the reference project
**Symptom:** `dotnet build Terrain.sln --no-restore` initially failed with `CS1503` around `CommandList.SetRenderTargets(...)`.

**Root Cause:** The reference renderer used an overload taking `(depth, count, span)`, while this project Stride build exposes `SetRenderTargets(Texture, ReadOnlySpan<Texture>)`.

**Solution:** Changed calls to:
- `SetRenderTargets(currentDepthStencil, CollectionsMarshal.AsSpan(currentRenderTargets))`
- `SetRenderTargets(null, context.CommandList.RenderTargets)`
- `SetRenderTargets(depthStencilROCached, context.CommandList.RenderTargets)`

---

## Verification

### Build
Command:

```powershell
dotnet build Terrain.sln --no-restore
```

Result: Passed.

Warnings remain from existing package advisories and existing code. This task does not add a new `CustomForwardRenderer` warning.

### Tests
Command:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Result: Failed as expected for future tasks.

Passing Task 2 checks included:
- `custom forward renderer owns water draw order`
- `runtime compositor uses custom forward renderer`
- `water rendering architecture avoids obsolete renderer names`

Remaining failures:
- `water refraction capture pass uses canonical image shader`
- `water refraction capture resources use half float target`
- `water refraction capture shader packs river world space`
- `ocean render feature exposes renderer callable water draw`
- `river render feature exposes renderer callable water chain`

These correspond to Task 3, Task 4, and Task 5.

---

## Architecture Impact

Runtime Game/Editor rendering now routes through `Terrain.Rendering.CustomForwardRenderer`, but actual water ordering is not active yet because the water hooks are no-op and generic transparent rendering still draws Ocean/River.

No ADR was created; this session implemented an already planned architecture slice and did not add a new standalone architectural decision.

---

## Next Session

1. Implement Task 3: `WaterRefractionCaptureResources`, `WaterRefractionCapturePass`, and `WaterRefractionCapture.sdsl`.
2. Implement Task 4: expose renderer-callable River water chain and remove private `RiverSceneSeed` capture ownership.
3. Implement Task 5: expose renderer-callable Ocean water draw and bind shared refraction capture.

---

## Session Statistics

**Files Changed:** 6
**Commits:** 0

---

## Quick Reference for Future Agents

- `CustomForwardRenderer` is intentionally compileable but incomplete.
- Do not infer that shared water refraction capture exists yet.
- Do not remove Ocean/River transparent selectors until the renderer-callable draw paths and double-draw prevention are implemented.
- The `WaterRefractionCapturePass` field is `object?` only to avoid creating the Task 3 type early.
