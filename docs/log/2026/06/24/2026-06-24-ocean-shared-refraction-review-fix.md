# Ocean Shared Refraction Review Fix
**Date**: 2026-06-24
**Session**: Task 6 P1 code quality fix
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Fix two reviewer P1 issues in the Ocean water path without renaming renderer/pass types:
- Ocean refraction payload decode must bind the same `_RefractionMaxCameraHeight` used by the shared capture.
- Ocean flowmap sampling must bind `_WaterFlowMapSize` from the loaded FlowMap texture dimensions instead of keeping the shader default `256x256`.

---

## Context & Background

Previous sessions moved River and Ocean onto `CustomForwardRenderer` and the renderer-owned `WaterRefractionCapturePass`. Ocean `OceanSurface` then started using `RiverDecompressWorldSpace`, but `OceanRenderFeature.DrawWater(...)` was not receiving or binding the capture clamp used when the alpha payload was written. Ocean flowmap interpolation also depended on `_WaterFlowMapSize`, while the render feature only bound the FlowMap texture.

Related sessions:
- `docs/log/2026/06/24/2026-06-24-river-shared-refraction-capture-task4.md`
- `docs/log/2026/06/24/2026-06-24-ocean-shared-refraction-capture-task5.md`
- `docs/log/2026/06/24/2026-06-24-ocean-ck3-core-water-task6.md`

---

## What We Did

### 1. Shared refraction clamp propagation
**Files Changed:** `Terrain/Rendering/Water/WaterRefractionCapturePass.cs`, `Terrain/Rendering/CustomForwardRenderer.cs`, `Terrain/Rendering/Ocean/OceanRenderFeature.cs`

- Extended `WaterRefractionCaptureResult` with `RefractionMaxCameraHeight`.
- Returned the exact `refractionMaxCameraHeight` used by `WaterRefractionCapturePass.Capture(...)`.
- Passed that value from `CustomForwardRenderer.DrawOceanWater(...)` into `OceanRenderFeature.DrawWater(...)`.
- Bound `RiverCommonKeys._RefractionMaxCameraHeight` on the Ocean effect before drawing.

### 2. FlowMap dimensions
**Files Changed:** `Terrain/Rendering/Ocean/OceanRenderFeature.cs`

- After binding `oceanResources.FlowMap`, bind `OceanSurfaceKeys._WaterFlowMapSize` from `oceanResources.FlowMap.ViewWidth/ViewHeight` when the texture is loaded.
- Existing skip behavior remains intact because `DrawWater(...)` and `Prepare(...)` still require `oceanResources.IsLoaded`.

### 3. Text regression coverage
**Files Changed:** `Terrain.Editor.Tests/OceanShaderTextTests.cs`

- Added assertions that `OceanRenderFeature.DrawWater(...)` receives and binds `refractionMaxCameraHeight`.
- Added assertions that `CustomForwardRenderer` reuses `WaterRefractionCaptureResult.RefractionMaxCameraHeight` for Ocean draw.
- Added assertion that `_WaterFlowMapSize` is bound from the actual FlowMap texture dimensions.

---

## Problems Encountered & Solutions

### Problem 1: Ocean decode could use a different clamp than capture
**Symptom:** `CustomForwardRenderer` computed a shared refraction clamp for capture, but Ocean draw only received texture and dimensions.
**Root Cause:** `WaterRefractionCaptureResult` did not carry the clamp forward, so the capture-time value was lost before Ocean draw.
**Solution:** Store the clamp in `WaterRefractionCaptureResult` and pass it into `OceanRenderFeature.DrawWater(...)`.

### Problem 2: FlowMap interpolation stayed on shader default dimensions
**Symptom:** `OceanSurface` declared `_WaterFlowMapSize = 256x256`, but the render feature did not override it from the loaded DDS.
**Root Cause:** Static resource binding set the FlowMap texture but not its dimensions.
**Solution:** Bind `_WaterFlowMapSize` from `oceanResources.FlowMap.ViewWidth/ViewHeight` when FlowMap is non-null.

---

## Verification

- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
  - Passed. Existing NuGet vulnerability warnings and existing compiler warnings only.
- `dotnet build Terrain.sln --no-restore`
  - Passed. Existing NuGet vulnerability warnings only in final build output.

No SDSL was changed, so no shader key generation was required.

---

## Architecture Impact

Ocean now uses the same shared refraction payload contract as River for alpha distance decode: the writer and reader share the same `_RefractionMaxCameraHeight`. Ocean flowmap interpolation also now reflects the actual runtime FlowMap texture size.

Updated:
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

No ADR was created; this is a correctness fix within the existing shared water rendering architecture.

---

## Quick Reference for Future Agents

- `WaterRefractionCaptureResult.RefractionMaxCameraHeight` is the canonical capture clamp to pass to Ocean.
- `OceanRenderFeature.DrawWater(...)` must bind `RiverCommonKeys._RefractionMaxCameraHeight` because `OceanSurface` decodes via `RiverDecompressWorldSpace`.
- `_WaterFlowMapSize` should come from `oceanResources.FlowMap.ViewWidth/ViewHeight`, not the shader default.
