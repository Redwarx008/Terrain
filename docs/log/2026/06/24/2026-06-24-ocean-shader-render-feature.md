# Ocean Shader Render Feature
**Date**: 2026-06-24
**Session**: task-8-ocean-shader-render-feature
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Add Ocean SDSL shaders and `OceanRenderFeature` without registering them into `MainScene.sdscene`, `GraphicsCompositor.sdgfxcomp`, or editor runtime wiring.

**Success Criteria:**
- `OceanSurface` samples all required shared water textures and uses scene-driven `RiverStrideLighting`.
- `OceanRenderFeature` draws `OceanRenderObject` with `OceanMaterialSettings` fields only.
- Generated `.sdsl.cs` key files are updated and registered in `Terrain.csproj`.
- Shader asset workflow, text tests, solution build, and diff checks pass.

---

## Context & Background

**Previous Work:**
- `OceanComponent`, `OceanProcessor`, `OceanRenderObject`, and `OceanResourceLoader` already existed.
- `WaterSceneLightingBinder` was extracted in the previous session for reuse by ocean rendering.

**Current State:**
- Ocean now has shader and render feature code, but it is intentionally not connected to scene/compositor assets.

---

## What We Did

### 1. Added Ocean SDSL
**Files Changed:** `Terrain/Effects/Ocean/OceanVertexStreams.sdsl`, `Terrain/Effects/Ocean/OceanSurface.sdsl`

**Implementation:**
- Added `OceanVertexStreams` with `stage stream float2 OceanUV : TEXCOORD0;`.
- Added `OceanSurface : ShaderBase, TransformationWAndVP, OceanVertexStreams, RiverStrideLighting`.
- Declared and sampled `WaterColorTexture`, `AmbientNormalTexture`, `FlowMapTexture`, `FlowNormalTexture`, `FoamTexture`, `FoamRampTexture`, `FoamMapTexture`, and `FoamNoiseTexture` through `OceanTextureSampler`.
- Used `RiverStrideComputeLighting`, `RiverStrideEvaluateSceneShadow`, and scene light direction from `RiverStrideLighting`.
- Exposed only the existing ocean material fields as shader params: `ShallowColor`, `DeepColor`, `_OceanRoughness`, and `_WaveScale`.

**Rationale:**
- Keep Task 8 focused on reusable render/shader infrastructure and avoid introducing Task 9 scene/runtime coupling.

### 2. Added OceanRenderFeature
**Files Changed:** `Terrain/Rendering/Ocean/OceanRenderFeature.cs`

**Implementation:**
- Supports only `OceanRenderObject`, with `SortKey = 180` so it sorts before river.
- Initializes `DynamicEffectInstance("OceanSurface")`, `OceanResourceLoader`, `WaterSceneLightingBinder`, and a single transparent-style pipeline state.
- `Prepare` reads the first enabled drawable ocean object and its source `OceanComponent.Material`, then binds material fields, sea level, and map size.
- `Draw` binds camera position, global time, view-projection, per-object world matrices, scene lighting, pipeline state, vertex/index buffers, and draws each enabled ocean object in the draw range.
- Static resource binding covers every `OceanResourceLoader` texture plus `OceanTextureSampler` and `RiverStrideLightingKeys.EnvironmentMapSampler`.

### 3. Registered Shader Keys and Tests
**Files Changed:** `Terrain/Terrain.csproj`, `Terrain.Editor.Tests/OceanShaderTextTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- Registered Ocean SDSL and generated `.sdsl.cs` files with River-style `Compile` / `None` metadata.
- Added text tests for required texture sampling, shared scene lighting use, CK3-only token avoidance, render feature resource/lighting binding, no scene/compositor registration, and project shader registration.
- Ran Stride generated-file update to create `OceanSurface.sdsl.cs` and `OceanVertexStreams.sdsl.cs`.

---

## Problems Encountered & Solutions

### Multi-targeted Stride asset targets
**Symptom:** The exact outer command `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` failed with `MSB4057` because the outer multi-target project did not expose `_StridePrepareAssetCompiler`.

**Root Cause:** `Terrain.csproj` uses `TargetFrameworks`; the Stride asset targets are imported in the inner `net10.0-windows` build.

**Solution:** Re-ran the same Stride workflow targets with `/p:TargetFramework=net10.0-windows`, which generated Ocean shader keys and completed clean/compile targets. This trap is recorded in `docs/log/learnings/stride-sdsl-material-cbuffer-linking.md`.

---

## Code Quality Notes

### Testing
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed.
- `dotnet build Terrain.sln --no-restore` passed.
- `git diff --check` passed with only CRLF normalization warnings for existing text files.

### Shader Asset Workflow
- `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` succeeded for `net10.0-windows` and processed `Effects/Ocean/OceanVertexStreams` and `Effects/Ocean/OceanSurface`.
- `StrideCleanAsset` succeeded for `net10.0-windows`.
- `StrideCompileAsset` succeeded for `net10.0-windows`.

---

## Next Session

### Immediate Next Steps
1. Task 9 should register `OceanRenderFeature` in the compositor and create/connect the runtime ocean scene entity.
2. Use runtime/editor smoke validation once ocean is actually wired into a render stage.

---

## Session Statistics

**Files Changed:** 11
**Commits:** 1 planned

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Ocean shader/render feature exist but are deliberately inactive until scene/compositor registration.
- Ocean material binding uses only `ShallowColor`, `DeepColor`, `Roughness`, and `WaveScale`.
- Stride asset targets for `Terrain.csproj` need `/p:TargetFramework=net10.0-windows` in CLI workflows.

**Gotchas for Next Session:**
- Do not add CK3-only province/border/fog-of-war/fixed sun tokens to ocean shader.
- Do not treat Task 8 as runtime proof; no scene/compositor wiring exists yet.
