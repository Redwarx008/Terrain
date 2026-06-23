# Ocean Shared Refraction Capture Task 5
**Date**: 2026-06-24
**Session**: Task 5 implementation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Migrate Ocean rendering from the generic transparent draw path to the `CustomForwardRenderer` Water-stage path, consuming the same shared refraction capture used by River and preventing transparent-stage double draw.

---

## Context & Background

Task 4 already moved River to `CustomForwardRenderer.DrawWaterChain(...)` and left Ocean as the expected Task 5 red light. The compositor had a dedicated Water stage, but Ocean still selected the generic Transparent stage.

Related session:
- `docs/log/2026/06/24/2026-06-24-river-shared-refraction-capture-task4.md`

---

## What We Did

### 1. Ocean renderer-callable draw
**Files Changed:** `Terrain/Rendering/Ocean/OceanRenderFeature.cs`

- Added `internal void DrawWater(...)` with the renderer-callable signature expected by the water renderer.
- Moved the old draw body into `DrawWater(...)`.
- Bound `OceanSurfaceKeys.RefractionTexture`, `RefractionSampler`, and `_RefractionTextureSize` from the shared capture.
- Left `Draw(...)` as an explicit no-op with a comment explaining that Ocean is driven by `CustomForwardRenderer` and Water stage to avoid selector residue double draw.

### 2. Minimal Ocean shared capture shader parameters
**Files Changed:** `Terrain/Effects/Ocean/OceanSurface.sdsl`, `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`

- Added:
  - `stage Texture2D<float4> RefractionTexture;`
  - `stage SamplerState RefractionSampler;`
  - `stage float2 _RefractionTextureSize = float2(1.0f, 1.0f);`
- Added a very light shared-capture sample blended into Ocean base color.
- Did not add CK3 province, border, fog-of-war, flatmap, or full core water composition.

### 3. Unified renderer-owned water capture
**Files Changed:** `Terrain/Rendering/CustomForwardRenderer.cs`

- Added Water-stage Ocean range collection using `SortedRenderNodes`.
- Refactored capture ownership so `CustomForwardRenderer` collects Ocean and River ranges first, then creates at most one shared refraction capture.
- Kept River camera-height skip River-specific:
  - If only River exists and camera is above every river visibility cutoff, capture is skipped.
  - If Ocean exists, capture still runs for Ocean even when River would be skipped.
- Refraction max camera height remains the max River requirement, defaulting to `50.0f` for Ocean-only ranges.
- Draw order is now Ocean first, then River bottom/surface.

### 4. Water-stage routing
**Files Changed:** `Terrain/Assets/GraphicsCompositor.sdgfxcomp`, `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

- Moved the runtime Ocean selector from Transparent stage to Water stage.
- Updated editor fallback compositor setup so `EnsureOceanRenderFeature` removes stale OceanSurface selectors on non-Water stages and adds the Water-stage selector.

### 5. Tests and docs
**Files Changed:** `Terrain.Editor.Tests/OceanShaderTextTests.cs`, `Terrain.Editor.Tests/RuntimeOceanAssetTests.cs`, `Terrain.Editor.Tests/WaterRenderingTextTests.cs`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

- Added text coverage for Ocean shared refraction shader parameters and C# bindings.
- Added coverage for Ocean Water-stage routing and renderer-owned single shared capture.
- Updated architecture/current feature docs from Task 5 pending to complete.

---

## Problems Encountered & Solutions

### Generated Ocean shader keys did not refresh automatically
**Symptom:** `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` succeeded, but `OceanSurface.sdsl.cs` did not gain the new refraction keys.

**Root Cause:** This matches the known Stride generated-file target behavior already recorded during Task 3: the CLI target can report success without rewriting checked-in generated key files.

**Solution:** Updated `OceanSurface.sdsl.cs` in the same generated-key style as the existing file, then verified with build and shader compile tests. C# references to `OceanSurfaceKeys.RefractionTexture`, `RefractionSampler`, and `_RefractionTextureSize` now compile.

### Direct `StrideAssetUpdateGeneratedFiles` target is not standalone here
**Symptom:** Running `dotnet msbuild Terrain\Terrain.csproj /t:StrideAssetUpdateGeneratedFiles ...` during diagnosis failed because the generated command was `dotnet "" --updated-generated-files ...`.

**Solution:** Used the project-required combined target form instead:
`dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`.

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet build Terrain.sln --no-restore`
  - Passed.
  - Existing warnings remain: NuGet vulnerability warnings, one nullable warning in `TerrainRenderFeature`, editor unused-field/event warnings, and WinForms DPI manifest warning.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
  - Passed.
  - Existing NuGet vulnerability warnings remain.

---

## Architecture Impact

Ocean and River now share the renderer-owned water refraction capture path. Generic Transparent no longer draws Ocean or River through their render-feature `Draw(...)` overrides; both retain selectors only to populate the dedicated Water stage through Stride collect/cull/sort.

No ADR was created because this implements the already planned Task 5 architecture.

---

## Next Session

Task 6 can implement the fuller CK3 core water shader semantics on top of the now-shared Ocean/River capture path.

---

## Session Statistics

**Files Changed:** 12
**Commits:** 0

---

## Quick Reference for Future Agents

- `CustomForwardRenderer.DrawWaterRefractionCapture(...)` now owns the single capture decision for both Ocean and River.
- `OceanRenderFeature.Draw(...)` is intentionally no-op; use `DrawWater(...)`.
- Ocean shader has only minimal shared refraction sampling. Do not treat it as full CK3 core water parity.
- The required generated-file target passed, but key file refresh still required manual sync in this repo state.
