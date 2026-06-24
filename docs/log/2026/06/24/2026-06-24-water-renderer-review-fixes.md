# Water Renderer Review Fixes
**Date**: 2026-06-24
**Session**: Subagent review follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Address subagent review findings for the `CustomForwardRenderer` shared water refraction path.

---

## What We Fixed

### 1. River shared capture clamp
**Files Changed:** `Terrain/Rendering/CustomForwardRenderer.cs`, `Terrain/Rendering/River/RiverRenderFeature.cs`

- Passed `WaterRefractionCaptureResult.RefractionMaxCameraHeight` into `RiverRenderFeature.DrawWaterChain(...)`.
- River bottom and surface now bind the same clamp used by the shared capture writer.
- Removed the per-range decode clamp recalculation inside `DrawWaterChain(...)`.

### 2. Explicit depth source for water capture
**Files Changed:** `Terrain/Rendering/Water/WaterRefractionCapturePass.cs`, `Terrain/Rendering/CustomForwardRenderer.cs`

- `WaterRefractionCapturePass.Capture(...)` now receives the renderer's current scene depth.
- Removed Presenter depth inference from the capture pass.
- Added depth/color size validation before resolving depth as SRV.

### 3. Ocean viewport-scaled refraction offset
**Files Changed:** `Terrain/Effects/Ocean/OceanSurface.sdsl`

- Replaced the hard-coded `1920/1080` refraction offset scale with `_ViewSize`.

### 4. MSAA behavior made explicit
**Files Changed:** `Terrain/Rendering/CustomForwardRenderer.cs`

- The custom renderer now fails fast for non-null MSAA output/depth targets.
- The guard preserves the existing null output/depth fallback path for temporary non-MSAA targets.

### 5. Regression coverage
**Files Changed:** `Terrain.Editor.Tests/*`

- Added `WaterRefractionCapture` to Stride shader compile regression.
- Added `Terrain/Effects/Water` to compile-test source directories.
- Added text coverage for shared River clamp, explicit capture depth source, Ocean `_ViewSize` refraction offset and MSAA fail-fast.
- Preserved the existing Ocean strict depth read and RGB-only color write tests.

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet build Terrain.sln --no-restore` passed with existing warnings only.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed with existing warnings only.
- `git diff --check` passed.
- Subagent re-review approved the fixes.

---

## Remaining Risk

- MSAA is explicitly unsupported by `CustomForwardRenderer`; enabling it now produces a clear exception instead of silent output loss.
- Full MSAA resolve/copy-back support remains a separate renderer feature.
- RenderDoc or screenshot validation is still needed for visual CK3 ocean parity.
