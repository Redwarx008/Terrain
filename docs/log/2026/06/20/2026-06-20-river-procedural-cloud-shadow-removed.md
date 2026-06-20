# River Procedural Cloud Shadow Removed
**Date**: 2026-06-20
**Session**: River surface deterministic post-processing
**Status**: ✅ Complete
**Priority**: High

---

## Superseded By

Later in the same day, `docs/log/2026/06/20/2026-06-20-river-surface-post-processing-removed.md` temporarily removed the entire `ApplySurfacePostProcessing` wrapper for isolation. That was then superseded by `docs/log/2026/06/20/2026-06-20-river-surface-post-processing-restored.md`: the current state restores deterministic terrain shadow tint and map distance fog, while procedural cloud shadow remains removed.

---

## Session Goal

**Primary Objective:**
- Remove procedural cloud shadow logic from `RiverSurface.sdsl` after RenderDoc hot replacement proved it caused time-dependent river darkening between `debug3.rdc` and `debug4.rdc`.

**Success Criteria:**
- River surface no longer computes `GetCloudShadowMask`.
- River surface no longer blends final RGB toward the procedural cloudy tint.
- Terrain shadow tint and map distance fog remain active.
- Stride shader keys/assets are refreshed.

---

## Context & Background

The previous RenderDoc MCP session isolated the color difference to `ApplySurfacePostProcessing`: `debug3` had `cloudMask=1`, while `debug4` had `cloudMask=0`. The procedural cloud mask was driven by `_GlobalTime`, so repeated terrain visibility toggles appeared to change river color even though bindings, resources, inputs, and `CalcRefraction` were stable.

---

## What We Did

### 1. Removed Procedural Cloud Shadow From RiverSurface
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- Removed `_InverseWorldSize`.
- Removed `_HasCloudShadowEnabled`.
- Removed `GetCloud` and `GetCloudShadowMask`.
- Removed cloud-only helper functions `Levels`, `LevelsScan`, and `Overlay`.
- Kept `CalcNoise`, because map distance fog still uses it.
- Changed `ApplySurfacePostProcessing` to use:

```hlsl
const float cloudMask = 0.0f;
color.rgb = ApplyTerrainShadowTintWithClouds(color.rgb, worldPosition.xz, cloudMask, 1.0f);
color.rgb = ApplyMapDistanceFogWithoutFoW(color.rgb, worldPosition);
```

This preserves deterministic terrain shadow tint and distance fog while removing time-varying cloud tint.

### 2. Removed Dead Parameter Binding
**Files Changed:**
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- Removed `parameters.Set(RiverSurfaceKeys._InverseWorldSize, worldSpaceToTerrain);`.
- `_WorldSpaceToTerrain0To1` remains bound for terrain shadow tint sampling.

### 3. Updated Regression Tests
**Files Changed:**
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- Updated post-step text test to require `const float cloudMask = 0.0f`.
- Added negative checks for `_HasCloudShadowEnabled`, `GetCloudShadowMask`, `_GlobalTime` cloud movement, cloud tint blend, and `_InverseWorldSize` feature binding.

### 4. Refreshed Shader Keys and Assets
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl.cs`

**Commands Run:**
```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

**Result:**
- Generated shader keys refreshed successfully.
- Stride asset compile succeeded.
- Existing HLSL warning `X3557: loop doesn't seem to do anything, forcing loop to unroll` remains unrelated.

---

## Problems Encountered & Solutions

### Problem 1: Test Runner Build Is Blocked By Existing Editor Compile Error
**Symptom:**
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug` fails before tests execute.

**Root Cause:**
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs(707,35)` references `ISceneRenderer.PostEffects`, which does not exist on that type.

**Impact:**
- Full test runner verification could not complete in this session.
- Shader asset generation and asset compilation still completed successfully.

**Next Step:**
- Fix the unrelated `EmbeddedStrideViewportGame` compile error, then rerun the test runner.

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decision
- River surface no longer attempts CK3-style procedural cloud shadow parity in the editor path.
- The editor path favors deterministic river color for capture comparison and visual debugging.
- Terrain shadow tint and map distance fog remain part of the post-processing wrapper.

---

## Next Session

### Immediate Next Steps
1. Fix `EmbeddedStrideViewportGame.cs` `ISceneRenderer.PostEffects` compile error.
2. Rerun `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`.
3. Capture a fresh frame and verify river surface no longer varies with procedural cloud phase.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Do not reintroduce `GetCloudShadowMask` into `RiverSurface.sdsl`.
- `_GlobalTime` still drives water flow/foam/waves; it should no longer drive surface wrapper cloud tint.
- `_WorldSpaceToTerrain0To1` is still required; `_InverseWorldSize` was cloud-only and removed.
