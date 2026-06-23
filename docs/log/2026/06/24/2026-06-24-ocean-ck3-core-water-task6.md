# Ocean CK3 Core Water Task 6
**Date**: 2026-06-24
**Session**: Task 6 implementation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Advance `OceanSurface` from the Task 5 simplified water/refraction blend to CK3 core water semantics, while keeping province, border, fog-of-war, flatmap, fixed-sun strategy tokens out of Ocean.

---

## Context & Background

Task 5 moved Ocean to the `CustomForwardRenderer` Water-stage path and bound the renderer-owned shared refraction capture. The shader still only did a lightweight capture sample. River already had validated helpers for unfiltered alpha payload reads, refraction base/offset separation, water fade, see-through, foam ramp clamp, normals, and scene lighting.

Related sessions:
- `docs/log/2026/06/24/2026-06-24-ocean-shared-refraction-capture-task5.md`
- `docs/log/2026/06/24/2026-06-24-river-shared-refraction-capture-task4.md`
- `docs/log/2026/06/21/2026-06-21-river-stride-standard-lighting.md`

---

## What We Did

### 1. Ocean core water shader
**Files Changed:** `Terrain/Effects/Ocean/OceanSurface.sdsl`

- Kept the requested mixin chain:
  `ShaderBase, TransformationWAndVP, OceanVertexStreams, RiverStrideLighting`.
- Kept the existing Ocean water DDS resources and shared refraction resources.
- Added stage parameters for view size/matrix, see-through density, refraction and fade shore masks, foam shore mask, three ambient wave layers, flow normal scale/flatten/time/map size, and fresnel/reflection controls.
- Added named helpers:
  - `ComputeRefractionPayloadCoord`
  - `SampleRefractionPayload`
  - `DecodeRefractionWorldPosition`
  - `CalcRefraction`
  - `ComputeWaterFade`
  - `ComputeRefractionShoreMask`
  - `CalcTerrainUnderwaterSeeThrough`
- `SampleRefractionPayload` reads alpha payload via `RefractionTexture.Load(...)`; RGB remains linearly sampled.
- `CalcRefraction` separates base sample/payload from offset sample/payload and uses `step(WorldSpacePos.y, offsetRefractionWorldPosition.y)` to reject offset payloads that decode above water.
- Ambient normal now combines three wave layers.
- Flow normal now interpolates four neighboring flowmap cells instead of using a single old flow sample.
- Foam uses shore-depth masking and `FoamRampTexture.SampleLevel(...)` with half-texel ramp clamp.
- Reflection/fresnel samples the environment map exposed by `RiverStrideLighting`.
- Final normal Ocean output alpha is `1.0f`.

### 2. Ocean render feature binding
**Files Changed:** `Terrain/Rendering/Ocean/OceanRenderFeature.cs`

- Bound new generated keys used per frame:
  - `_ViewSize`
  - `_ViewMatrix`
  - `_WaterFlowTime`
- Continued binding shared capture texture, sampler, refraction size, camera position, global time, scene lighting, and per-object transforms.

### 3. Text coverage and generated keys
**Files Changed:**
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`

- Updated Ocean shader tests to assert the CK3 core water tokens and forbid strategy-only tokens.
- Ran the required generated-file target before manual edits:
  `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- The target succeeded but did not add the new `OceanSurfaceKeys` entries. This matches the Task 5 generated-file no-op behavior.
- Manually synced `OceanSurface.sdsl.cs` in the existing generated key format after the target ran.

---

## Problems Encountered & Solutions

### Generated Ocean keys did not refresh
**Symptom:** `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` returned success, but `OceanSurface.sdsl.cs` still only contained the Task 5 refraction keys.

**Root Cause:** Existing repository behavior: Stride's CLI generated-file target may no-op for checked-in shader key files.

**Solution:** After running the target, manually synced the generated key file with the new Ocean stage parameters and confirmed C# compilation.

### Text test overfit to old sampling form
**Symptom:** The first editor test run failed because `OceanShaderSamplesRequiredWaterTextures` expected every texture to appear as direct `<Texture>.Sample`.

**Root Cause:** The new shader samples `AmbientNormalTexture` through a generic helper and reads `FlowMapTexture` with `Load` for four-cell interpolation.

**Solution:** Updated the test to assert semantic tokens: ambient helper call, flowmap `Load`, direct flow normal sampling, and foam/water/refraction sampling.

---

## Verification

- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
  - Passed.
- `dotnet build Terrain.sln --no-restore`
  - Passed before the final test adjustment; existing warnings only.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
  - Passed after the test adjustment; existing NuGet vulnerability warnings only.

No RenderDoc capture was performed, per task scope.

---

## Architecture Impact

Ocean now consumes the shared water capture with the same payload discipline as River surface: RGB can be filtered, alpha payload must be loaded unfiltered before world-position decode. The Ocean shader remains renderer-owned Water-stage work and still excludes strategy-layer map inputs.

No ADR was created because this is an implementation step in the existing water rendering architecture.

---

## Next Session

Recommended follow-up is overall visual/GPU validation with RenderDoc or screenshot comparison across Ocean + River together, after Task 6 is integrated with the broader water rendering chain.

---

## Session Statistics

**Files Changed:** 7 task files/docs in this session
**Commits:** 0

---

## Quick Reference for Future Agents

- `OceanSurface.sdsl` now has core helper names that mirror River surface concepts but remains Ocean-specific.
- `OceanSurface.sdsl.cs` was manually synced after the required generation target no-op.
- Do not add `ProvinceColor`, `BorderDistanceField`, `FogOfWar`, `FlatMap`, `_WaterToSunDir`, or `_DefaultEnvironmentSun` to Ocean.
- Visual validation was intentionally deferred; this task used shader compile, asset compile, build, and editor text tests.
