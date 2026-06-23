# River Height Source Bilinear Fix
**Date**: 2026-06-23
**Session**: River mountain stair-step diagnosis
**Status**: ✅ Complete, with follow-up regression fixed
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Diagnose and fix river height stair-stepping visible on mountain terrain in `C:\Users\Redwa\Desktop\debug.rdc`.

**Success Criteria:**
- Confirm whether the artifact is mesh height data or shader/refraction.
- Fix the height sampling path if it still uses nearest terrain samples.
- Add a regression test for the real editor/runtime source shape.

---

## Context & Background

**Previous Work:**
- `docs/ARCHITECTURE_OVERVIEW.md` already recorded that river centerlines should resample terrain height bilinearly after smoothing.
- Existing tests covered bilinear behavior through a stub height source, but did not cover a source whose `SampleHeight(float,float)` itself returns nearest integer samples.

**Current State:**
- RenderDoc capture opened successfully as D3D11 with 67 draws and no HIGH severity messages.
- River draws were identified at EID 276/323 and 290/343.

---

## What We Did

### 1. RenderDoc Diagnosis
**Files Inspected:** `C:\Users\Redwa\Desktop\debug.rdc`

**Findings:**
- Exported post-VS river meshes for EID 276, 290, 323, and 343.
- The same geometry was present in bottom/surface pairs.
- Centerline Y values showed large slope discontinuities, confirming the artifact was already in generated mesh height data rather than only in the surface shader.

### 2. Source Diagnosis
**Files Inspected:**
- `Terrain/Rivers/RiverMeshService.cs`
- `Terrain.Editor/Services/TerrainManager.cs`
- `Terrain/Rendering/River/RiverProcessor.cs`

**Root Cause:**
- Runtime `RiverProcessor` uses `new RiverMeshService(terrainComponent.GetHeight, ...)`, which goes through the mesh service's bilinear helper.
- Editor uses `new RiverMeshService(TerrainManager)`, where `TerrainManager` implements `IRiverTerrainHeightSource.SampleHeight` by calling `GetHeightAtPosition`.
- `TerrainManager.GetHeightAtPosition` rounds `worldX/worldZ` to integer height samples.
- `RiverMeshService` delegated directly to `heightSource.SampleHeight(wx,wz)`, so the editor path bypassed mesh-service bilinear resampling.

### 3. Fix
**Files Changed:** `Terrain/Rivers/RiverMeshService.cs`

**Implementation:**
- `RiverMeshService.SampleTerrainHeight` now sends `IRiverTerrainHeightSource` through a new `SampleHeightSourceBilinear` path.
- The bilinear path samples the source only at integer corner coordinates and blends in `RiverMeshService`.
- This keeps `TerrainManager.GetHeightAtPosition` unchanged for editor raycast/picking semantics while smoothing river mesh generation.

### 4. First Regression Test
**Files Changed:** `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`

**Implementation:**
- Added `curved river centerline bilinearly resamples nearest height source`.
- The test uses a height source that intentionally rounds `worldX/worldZ`, matching the editor failure mode.
- The test failed before the fix with max height error `8.680`, then passed after the fix.

### 5. Follow-up: River Disappearing Near Camera
**Files Inspected:** `C:\Users\Redwa\Desktop\debug2.rdc`

**Findings:**
- River bottom/surface draws were present, and the surface shader/resource path was active.
- Pixel debugging on river surface and bottom showed `POSITION_WS.y = 0.02`.
- That value is only the river `SurfaceOffset`, proving the generated river mesh no longer sampled terrain height in the editor path.

**Root Cause:**
- `EmbeddedStrideViewportGame` constructs `RiverMeshService` before the terrain heightmap is loaded.
- The first bilinear fix read `heightSource.HeightmapWidth/HeightmapHeight` from constructor-cached fields.
- In the editor, those cached dimensions were still `0`, so `SampleTerrainHeight` returned `0.0f` and every river vertex stayed at `0.02`.

**Fix:**
- `RiverMeshService.SampleTerrainHeight` now reads `IRiverTerrainHeightSource.HeightmapWidth/HeightmapHeight` at sampling time.
- `SampleHeightSourceBilinear` receives the current source dimensions instead of using constructor-time dimensions.

### 6. Follow-up Regression Test
**Files Changed:** `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`

**Implementation:**
- Added `river centerline uses height source dimensions loaded after mesh service construction`.
- The test constructs `RiverMeshService` before the fake height source loads dimensions and data, then verifies the generated centerline uses bilinear terrain heights after load.

### 7. Subagent Review Follow-up
**Reviewer Finding:**
- `SampleTerrainHeight` used dynamic `IRiverTerrainHeightSource` dimensions, but `GetMapWorldSize()` still preferred constructor-cached dimensions.

**Impact:**
- If the editor height source reloaded to a different size, Y sampling could use the new dimensions while `MapWorldSize`, `MapExtent`, and normalized `RiverVertex.Width` still used stale dimensions.

**Fix:**
- `GetMapWorldSize()` now uses current `IRiverTerrainHeightSource.HeightmapWidth/HeightmapHeight` whenever the service was constructed with a height source.
- Constructor-cached dimensions remain only for the runtime `Func<int,int,float>` path.

**Regression Test:**
- Added `river mesh map extent uses reloaded height source dimensions`.
- The test constructs the mesh service after an initial 5x9 load, reloads the height source to 9x17, then asserts `MapWorldSize`, `MapExtent`, and normalized vertex width use the reloaded dimensions.

---

## Verification

- `dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug /p:OutputPath="E:\Stride Projects\Terrain\.testrun\a\b\c\d\" /p:AppendTargetFrameworkToOutputPath=false`
  - Result: passed.
- `E:\Stride Projects\Terrain\.testrun\a\b\c\d\Terrain.Editor.Tests.exe`
  - Result: passed.
  - Existing NuGet vulnerability and nullable/compiler warnings remain.

---

## Architecture Impact

### Documentation Updates
- Updated `docs/ARCHITECTURE_OVERVIEW.md` with the 2026-06-23 river height sampling clarification.
- Updated `docs/CURRENT_FEATURES.md` with the editor/runtime height source consistency note.

### Decisions
- No new ADR required. This is a correction to the existing river mesh height sampling contract.

---

## Next Session

### Immediate Next Steps
1. Re-run the editor/runtime and capture a fresh frame if visual confirmation is needed.
2. Compare the new river post-VS mesh against the old capture to confirm slope discontinuities are reduced.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain/Rivers/RiverMeshService.cs`, `SampleTerrainHeight` and `SampleHeightSourceBilinear`.
- Key test: `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`, `CurvedRiverCenterlineBilinearlyResamplesNearestHeightSource`.
- Do not change `TerrainManager.GetHeightAtPosition` just to fix river mesh height sampling; that method is also used by editor interaction paths.

---

## Notes

- Temporary RenderDoc mesh JSON exports were deleted after diagnosis.
- The capture itself was not modified.
