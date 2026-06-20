# River Surface CalcWater Hot-Replace Gate
**Date**: 2026-06-18
**Session**: River surface CalcWater hot-replace gate
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Prove or disprove, in RenderDoc, whether target-style water constants and see-through attenuation can move the current river surface away from the blue high-energy output before editing SDSL.

**Success Criteria:**
- Current-like replacement samples event `305` refraction correctly.
- Target-style replacement produces dark, non-blue water-range samples.
- Resource and cbuffer inputs are traceable.
- Do not claim full shader semantic parity until `CalcWater` / `CalcRiverAdvanced` are ported as functions.

---

## What We Did

### 1. RenderDoc gate workspace
**Files Changed:** `artifacts/renderdoc/river_surface_calcwater_gate/*`

- Created local capture environment metadata and sample points.
- Exported current surface, target surface, target bottom, and current refraction-control PNGs.
- Dumped target water cbuffer and shader/resource evidence for target event `466`.

### 2. Reusable sample comparison tool
**Files Changed:** `tools/river/compare_river_surface_samples.py`

- Added a small Pillow-based sample comparator.
- Committed as `5331291 test: add river surface sample comparison tool`.

### 3. Current-like RenderDoc hot replacement
**Files Changed:** `artifacts/renderdoc/river_surface_calcwater_gate/surface_current_like.hlsl`

- Replaced current event `305` PS with a direct refraction sampler.
- Fixed the control shader to output alpha `1.0`; passing through refraction alpha produced black because the source alpha is a camera-relative distance payload consumed by the current blend state.
- `current-like-hotreplace-report.json`: `worst_max_abs_delta = 0.001922`.

### 4. Target-style energy gate
**Files Changed:** `artifacts/renderdoc/river_surface_calcwater_gate/surface_calcwater_gate.hlsl`

- Built a simplified target-style gate using target water color constants and exponential see-through attenuation.
- `calcwater-hotreplace-report.json`: all five samples passed the dark-water gate. Sample RGBs were around `[0.019608, 0.015686..0.019608, 0.015686..0.019608]`.
- This proves the current surface can be moved into the target energy range, but it is not a complete `CalcWater` semantic port.

### 5. Resource sync audit
**Files Changed:** `Terrain.Editor/Assets/River/Water/flow-map.dds`, `artifacts/renderdoc/river_surface_calcwater_gate/resource-sync-audit.json`

- Synced external water setting resources into existing neutral project paths.
- Existing `water-color`, `ambient-normal`, `flow-normal`, `foam`, `foam-ramp`, `foam-map`, and `foam-noise` DDS files already matched by SHA256.
- Added missing `flow-map.dds` under the current water asset directory for the upcoming full water path, but did not add `.sdtex` or bind it yet.

### 6. SDSL CalcWater structure port
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl.cs`, `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`, `Terrain.Editor/Rendering/River/RiverRenderObject.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

- Added a failing shader text test first for target-style `CalcWater` structure, then ported the surface shader.
- Replaced the old direct `PSMain` water composition with `PSMain -> CalcRiverAdvanced -> CalcWater`.
- Added SDSL-compatible function boundaries for foam, refraction, reflection, gloss, light composition, and water output.
- Synced `WaterColorShallow/Deep` defaults in shader and C# settings/object code to target cbuffer dark values.
- Added shader keys and bindings for water specular/gloss/cubemap/foam controls.
- Kept the implementation honest: SDSL does not accept the target struct-return form, so `CalcWater` uses out parameters; `FlowMapTexture`, FoW/cloud/fog exact scene masks, and full shader visual parity still need follow-up validation.

---

## Findings

### Current surface is not semantically equivalent

`Terrain.Editor/Effects/RiverSurface.sdsl` currently hand-rolls a water path in `PSMain` instead of matching the target structure:

- Current: `PSMain` manually combines `SampleFlowNormal`, `SampleAmbientNormal`, `ComputeFoam`, `SampleRefractionSeeThrough`, cubemap reflection, and neutral lighting.
- Target: `CalcRiverAdvanced` builds `SWaterParameters`, then calls `CalcWater`, which owns normal composition, foam, diffuse, lighting, refraction, water fade, reflection, and Fresnel composition.

Important mismatches:

- `WaterColorShallow/Deep` defaults in current SDSL are bright blue; target cbuffer values are very dark.
- Current foam uses a project-specific bank/connection formula; target uses `CalcFoamFactor` with `FoamNoiseTexture`, `FoamMapTexture`, `FoamTexture`, and `FoamRampTexture` in a different sequence.
- Current lighting uses `RiverApplyNeutralLighting`; target uses `SWaterLightingProperties`, `CalculateSunLight`, ambient SH-style lighting, specular factor, gloss scaling, and cloud/fog masks.
- Current reflection samples cubemap directly with gloss-map scaling; target flattens reflection normal and gates cubemap intensity through water/cloud parameters.
- Current alpha is computed separately and is not the target `WaterFade` until later edge fade logic.

### Resource table correction

The target shader disassembly confirms texture declarations:

- `WaterColorTexture_Texture (t3)`
- `AmbientNormalTexture_Texture (t4)`
- `FlowNormalTexture_Texture (t5)`
- `ReflectionCubeMap_Texture (t6)`
- `FoamTexture_Texture (t7)`
- `FoamRampTexture_Texture (t8)`
- `FoamMapTexture_Texture (t9)`
- `FoamNoiseTexture_Texture (t10)`
- `RefractionTexture_Texture (t11)`

The external settings files point `FoamTexturePath` to `foam.dds` and `FoamRampTexturePath` to `foam_ramp.dds`, both already matching the project files. The earlier metadata that inferred different t7/t8 sizes should not be used as a file replacement decision.

---

## Gate Result

- Current-like control: passed, `worst_max_abs_delta = 0.001922`.
- Target-style output: passed the dark-water energy gate, all samples below `[0.09, 0.12, 0.13]` and blue/green ratio <= `1.4`.
- Gate decision: proceeded to SDSL work by porting the target-style `CalcWater` structure rather than tuning the old hand-rolled path.

## Verification

- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` first failed on the new `SurfaceShaderUsesTargetCalcWaterStructure` test, as expected.
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` passed after the implementation.
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` passed.
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug` passed.
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug` passed with 911 succeeded, 0 failed; HLSL emitted one loop-unroll warning.
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug` passed with existing warnings.
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build` passed.

---

## Next Session

### Immediate Next Steps
1. Re-capture current river surface after the SDSL port and compare the same sample points against `ck3-river.rdc`.
2. Add `flow-map.sdtex` and package root asset only if the shader actually binds `FlowMapTexture`.
3. Decide whether FoW/cloud/fog masks need exact equivalents for the editor scene or should remain neutralized.
4. If the new capture still differs, trace `CalcWater` terms one at a time instead of returning to scalar color tuning.

### Gotchas
- Do not pass refraction alpha through a replacement shader unless the blend path expects a distance payload.
- Do not overwrite foam/ramp resources based only on inferred RenderDoc resource IDs; use shader declarations plus external settings paths.
- Do not claim full parity from the current gate shader. It is an energy proof, not a complete shader port.

---

## Session Statistics

**Files Changed:** diagnostic artifacts, one reusable tool, one temporary DDS resource, one session log
**Commits:** 1 during this gate (`5331291`); later SDSL implementation changes are currently uncommitted
