# River Stride Standard Lighting
**Date**: 2026-06-21
**Session**: 1
**Status**: Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Make river bottom and river surface use the same Stride standard-material lighting equations instead of separate river-local light models.

**Success Criteria:**
- Bottom and surface both receive scene sun, shadow cascade data, and scene skybox cubemap from `RiverRenderFeature`.
- River-specific refraction, foam, water fade, reflection, depth payload, and custom pass order remain intact.
- Stride shader key generation and asset compilation pass.

---

## Context & Background

RenderDoc comparison of `debug1.rdc` without terrain and `debug2.rdc` with terrain showed that the river draw outputs were not actually darker in the terrain capture. The visual compression came from terrain producing much higher HDR values through Stride standard material lighting, while river used a lower-energy custom lighting path. The fix was therefore to make river lighting use the same Stride-style Lambert/GGX/IBL model rather than adding a brightness multiplier.

Related design:
- `docs/superpowers/specs/2026-06-21-river-stride-lighting-design.md`
- `docs/superpowers/plans/2026-06-21-river-stride-lighting-plan.md`

---

## What We Did

### 1. Shared River Lighting Mixin
**Files Changed:** `Terrain.Editor/Effects/RiverStrideLighting.sdsl`

- Added `RiverStrideLighting` as a shared SDSL mixin.
- Implemented Stride-style direct Lambert, GGX direct specular, Schlick Fresnel, Smith-Schlick visibility, polynomial DFG, and skybox specular helpers. Diffuse IBL remains zero for the current scene because the scene skybox only provides specular lighting parameters.
- Moved the river scene shadow path into the shared helper as `RiverStrideEvaluateSceneShadow(...)`.
- Kept the CK3-style bottom shadow projection semantics, including the water-surface exclusion, random disc kernel, bias, and cascade-edge fade.

### 2. Bottom And Surface Shader Ports
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl`

- `RiverBottom` now mixes in `RiverStrideLighting` and routes `CalculateRiverBottomLighting(...)` through `RiverStrideComputeLighting(...)`.
- Removed bottom-local GGX, dominant-reflection IBL, scene sun, skybox, and shadow parameter declarations.
- `RiverSurface` now mixes in `RiverStrideLighting` and no longer declares `_DefaultEnvironmentSunDiffuse`, `_DefaultEnvironmentSunIntensity`, or `_WaterToSunDir`.
- Removed surface-local `ImprovedBlinnPhong`, `FresnelSchlick`, `GetNonLinearGlossiness`, and `ComposeLight`.
- Preserved surface refraction, foam, water fade, cubemap reflection, alpha, and depth payload behavior.

### 3. Scene Lighting Binding
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

- Renamed scene lighting prep to `PrepareRiverSceneLighting(...)`.
- Bound the same scene directional light, shadow cascade data, shadow map texture, skybox matrix, skybox intensity, mip count, and environment texture to both bottom and surface effects via `RiverStrideLightingKeys`.
- Kept the real scene skybox requirement instead of falling back to the river reflection cubemap.

### 4. Shader Asset Registration And Tests
**Files Changed:** `Terrain.Editor/Terrain.Editor.csproj`, generated `*.sdsl.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

- Registered `RiverStrideLighting.sdsl` for Stride shader key generation.
- Added generated `RiverStrideLighting.sdsl.cs`.
- Updated text tests to lock the shared lighting helper, shared bindings, removed river-local surface sun, and both shader compile paths.

---

## Problems Encountered & Solutions

### Problem 1: Shared Mixin Could Not Resolve `streams.DepthVS`
**Symptom:** `RiverBottom` and `RiverSurface` shader compile tests failed with `Unable to find stream variable [DepthVS] in class [RiverStrideLighting]`.

**Root Cause:** The shared mixin is compiled independently enough that it cannot assume `streams.DepthVS` exists, even though composed river shaders have it through the transform path.

**Solution:** Make `RiverStrideEvaluateSceneShadow(...)` accept `depthVS` as a parameter and pass `streams.DepthVS` from `RiverBottom` / `RiverSurface` call sites.

### Problem 2: Red Tests Became Too Format-Sensitive
**Symptom:** Early tests asserted entire single-line lighting calls and exact YAML spacing in `.sdpkg`.

**Root Cause:** The tests encoded formatting choices rather than behavior.

**Solution:** Split surface lighting assertions into smaller semantic fragments and check `!dir Effects` instead of an exact YAML line.

### Problem 3: First Pass BRDF Helpers Were Close But Not Stride-Exact
**Symptom:** Code review found the initial shared helper missed Stride details in Smith-Schlick visibility, DFG polynomial inputs/output, and roughness cubemap mip selection.

**Root Cause:** The first pass copied the broad GGX/IBL structure but simplified several helper equations.

**Solution:** Updated `RiverStrideVisibilitySmithSchlickGGX` to divide by `nDotL * nDotV`, changed DFG to Stride's `x = 1 - alphaRoughness`, `y = nDotV`, and `bias *= saturate(50 * specularColor.y)`, and changed specular IBL mip to `sqrt(alphaRoughness) * _EnvironmentMipCount`. Added a numeric test that compares visibility, DFG, and mip samples against the Stride formulas.

---

## Verification

Commands run from `.worktrees/river-stride-lighting`:

- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
- `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`

Result:
- All commands passed.
- Warnings remained from known NuGet vulnerability advisories and an existing asset compiler loop warning.
- Runtime RenderDoc verification was not performed in this session.

---

## Architecture Impact

- River lighting now has one shared Stride-style lighting model for bottom and surface.
- Bottom and surface still remain custom SDSL passes; this does not replace them with Stride `MaterialPass`.
- Scene light/skybox/shadow data are shared bindings, not bottom-only bindings.

---

## Next Session

1. Capture a new terrain-enabled RenderDoc frame and compare river pre-tonemap values against terrain under the same scene lighting.
2. If visual mismatch remains, separate material input differences from refraction-source timing differences.
3. Watch the surface specular factor path; it intentionally preserves `_WaterSpecularFactor` as water tuning on top of the shared direct specular helper.

---

## Quick Reference for Future Claude

**What Changed Since Last Doc Read:**
- New shared shader: `Terrain.Editor/Effects/RiverStrideLighting.sdsl`
- Bottom/surface now mix in `RiverStrideLighting`.
- C# scene lighting binding uses `RiverStrideLightingKeys` for both bottom and surface.

**Gotchas:**
- `RiverStrideEvaluateSceneShadow(...)` takes `depthVS` as an explicit parameter because shared mixins cannot directly assume `streams.DepthVS`.
- Do not reintroduce surface `_DefaultEnvironmentSunDiffuse`, `_DefaultEnvironmentSunIntensity`, `_WaterToSunDir`, or `ImprovedBlinnPhong`.
- Do not treat generated shader key churn as optional after SDSL parameter changes.
