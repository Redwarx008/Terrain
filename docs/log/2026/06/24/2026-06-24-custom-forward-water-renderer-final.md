# Custom Forward Water Renderer Final
**Date**: 2026-06-24
**Session**: Final integration
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Implement the master-branch water rendering change requested for CK3-like core ocean water:
- Use `CustomForwardRenderer` to own water ordering.
- Share one refraction capture between Ocean and River.
- Keep the helper named `WaterRefractionCapturePass`, not renderer.
- Move Ocean closer to CK3 core water without importing strategy overlay tokens.

---

## Context & Background

Previous analysis compared local Ocean against CK3 ocean draw 1061 and showed that the old Ocean path was much simpler than CK3 core water. The user also clarified that refraction capture should be shared with River and that the renderer should be named `CustomForwardRenderer`.

Related sessions:
- `2026-06-24-custom-forward-renderer-task2.md`
- `2026-06-24-water-refraction-capture-task3.md`
- `2026-06-24-river-shared-refraction-capture-task4.md`
- `2026-06-24-ocean-shared-refraction-capture-task5.md`
- `2026-06-24-ocean-ck3-core-water-task6.md`
- `2026-06-24-ocean-shared-refraction-review-fix.md`

---

## What We Did

### 1. Custom renderer and Water stage
**Files Changed:** `Terrain/Rendering/CustomForwardRenderer.cs`, `Terrain/Assets/GraphicsCompositor.sdgfxcomp`, `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

- Added `CustomForwardRenderer` based on the project's forward renderer needs.
- Added a dedicated Water render stage.
- Migrated Game, SingleView, Editor and editor fallback compositor paths to `CustomForwardRenderer`.
- Routed River and Ocean selectors to Water stage while keeping their `Draw(...)` callbacks inert to avoid double draw.

### 2. Shared refraction capture
**Files Changed:** `Terrain/Rendering/Water/WaterRefractionCapturePass.cs`, `Terrain/Rendering/Water/WaterRefractionCaptureResources.cs`, `Terrain/Effects/Water/WaterRefractionCapture.sdsl`

- Added a renderer-owned half-resolution `R16G16B16A16_Float` refraction capture.
- Captured scene color and depth after opaque rendering.
- Packed world-space refraction payload using River's common compression/decompression contract.
- Restored render targets after capture.

### 3. River shared capture migration
**Files Changed:** `Terrain/Rendering/River/RiverRenderFeature.cs`, `Terrain/Rendering/River/RiverRenderResources.cs`

- Removed the private `RiverRenderFeature` scene-seed image-effect path from runtime drawing.
- Added renderer-callable `DrawWaterChain(...)`.
- Copied the shared capture into River's working bottom buffer before bottom/surface rendering.
- Preserved River max-visible-camera-height skip logic before capture.

### 4. Ocean shared capture and CK3 core water
**Files Changed:** `Terrain/Rendering/Ocean/OceanRenderFeature.cs`, `Terrain/Effects/Ocean/OceanSurface.sdsl`, `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`

- Added renderer-callable `DrawWater(...)`.
- Bound shared refraction texture, sampler, dimensions, view data and flow time.
- Implemented CK3-like core ocean structure: unfiltered alpha payload `Load`, base/offset refraction, above-water offset rejection, see-through attenuation, water fade, shore masks, four-cell flow normal interpolation, three ambient normal layers, shore foam and fresnel/environment reflection.
- Kept strategy overlay tokens out of Ocean.

### 5. Review fixes
**Files Changed:** `WaterRefractionCapturePass.cs`, `CustomForwardRenderer.cs`, `OceanRenderFeature.cs`, `OceanShaderTextTests.cs`

- Extended `WaterRefractionCaptureResult` with `RefractionMaxCameraHeight`.
- Passed the capture-time clamp into Ocean and bound `RiverCommonKeys._RefractionMaxCameraHeight` before decode.
- Bound `_WaterFlowMapSize` from the loaded FlowMap texture dimensions instead of relying on the shader default.

---

## Decisions Made

### Decision 1: Renderer owns capture and ordering
**Decision:** `CustomForwardRenderer` owns the water capture and explicitly draws Ocean then River.
**Rationale:** Stride render features still participate in collect/cull/sort through Water stage, while the renderer controls the shared capture lifetime and order.

### Decision 2: Capture result carries decode clamp
**Decision:** `WaterRefractionCaptureResult` carries `RefractionMaxCameraHeight`.
**Rationale:** The writer and all readers must use the same compression/decompression range. Recomputing in Ocean would risk mismatch.

---

## Problems Encountered & Solutions

### Problem 1: Ocean initially decoded with default refraction clamp
**Symptom:** Code-quality review found Ocean did not bind the same `_RefractionMaxCameraHeight` used by capture.
**Solution:** Carry the clamp in `WaterRefractionCaptureResult` and bind it through `OceanRenderFeature.DrawWater(...)`.

### Problem 2: FlowMap integer loads used default 256x256 dimensions
**Symptom:** FlowMap texture is larger than the shader default, so `Load` coordinates would sample only the top-left region.
**Solution:** Bind `_WaterFlowMapSize` from `oceanResources.FlowMap.ViewWidth/ViewHeight`.

### Problem 3: Stride generated key target no-op
**Symptom:** `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` succeeded but did not refresh checked-in generated shader keys.
**Solution:** Manually synchronized generated key files after running the required target, then verified through asset compile, build and tests.

---

## Architecture Impact

- Runtime water now has a renderer-owned shared refraction capture.
- River and Ocean both consume the same capture source.
- Ocean now uses the RiverCommon payload contract for refraction world-position decode.
- Documentation updated in `docs/ARCHITECTURE_OVERVIEW.md` and `docs/CURRENT_FEATURES.md`.

No ADR was created; this is an implementation of the existing water-rendering direction rather than a new independent architectural decision.

---

## Code Quality Notes

### Testing
- Added `WaterRenderingTextTests`.
- Updated Ocean/River runtime asset and shader text tests.
- Added text coverage for renderer naming, Water stage routing, shared capture use, Ocean CK3 core tokens, forbidden strategy tokens, shared refraction clamp binding and real FlowMap dimension binding.

### Verification
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet build Terrain.sln --no-restore` passed with existing NuGet vulnerability warnings only.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed with existing NuGet vulnerability warnings only.

---

## Next Session

1. Capture a fresh RenderDoc frame for Ocean + River together and compare visual output against CK3 ocean draw 1061.
2. Decide whether old `RiverSceneSeed` shader/key/test assets should be cleaned up now that runtime water no longer creates that effect.
3. Tune Ocean constants after GPU visual validation rather than from text tests alone.

---

## Session Statistics

**Files Changed:** 25+ implementation, tests and docs files
**Commits:** 0 before final integration commit

---

## Quick Reference for Future Agents

- `CustomForwardRenderer` is the owning renderer.
- `WaterRefractionCapturePass` is a helper pass, not a renderer.
- `WaterRefractionCaptureResult.RefractionMaxCameraHeight` is the canonical decode clamp for Ocean.
- `OceanRenderFeature.Draw(...)` and `RiverRenderFeature.Draw(...)` intentionally no-op; renderer calls `DrawWater(...)` and `DrawWaterChain(...)`.
- Old `RiverSceneSeed` files may still exist, but current runtime water path does not create `new ImageEffectShader("RiverSceneSeed")`.
