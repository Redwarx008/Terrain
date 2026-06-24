# Water Renderer Review Fixes 2
**Date**: 2026-06-24
**Session**: Subagent review follow-up 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Address the second subagent review pass for the shared water refraction path.

---

## What We Fixed

### 1. Ocean sea level capture clamp
**Files Changed:** `Terrain/Rendering/CustomForwardRenderer.cs`, `Terrain/Rendering/Ocean/OceanRenderFeature.cs`

- `CustomForwardRenderer.ResolveWaterRefractionMaxCameraHeight()` now includes Ocean draw ranges before merging River draw ranges.
- `OceanRenderFeature.GetRefractionMaxCameraHeight(...)` resolves the largest enabled Ocean sea level in the current Water-stage range.
- The Ocean clamp uses `SeaLevel + 1.0f` padding so Ocean-only high sea levels use the same shared capture decode height as water readers.

### 2. River viewport-scaled refraction offset
**Files Changed:** `Terrain/Effects/River/RiverSurface.sdsl`

- Replaced the hard-coded `1920/1080` refraction offset scale with `_ViewSize`.
- River now matches the Ocean refraction offset scaling contract for active viewport size.

### 3. Regression coverage
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`, `Terrain.Editor.Tests/WaterRenderingTextTests.cs`

- Added River shader text assertions for `_ViewSize` refraction offset and forbidding `1920.0f`/`1080.0f`.
- Added CustomForwardRenderer/OceanRenderFeature text coverage for including Ocean sea level in the shared capture clamp.

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet build Terrain.sln --no-restore` passed with existing warnings only.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed with existing warnings only.
- `git diff --check` passed.
- Subagent re-review found no blocking issues.

---

## Remaining Risk

- This pass validates shader compile/text behavior, not a new RenderDoc visual comparison.
- The Ocean clamp has text and call-order coverage; a future non-text renderer unit test would be stronger if constructing `RenderViewStage` becomes practical.
