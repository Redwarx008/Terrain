# Ocean debug1/debug2 dark output and speed fix
**Date**: 2026-06-24
**Session**: debug1 RenderDoc follow-up
**Status**: Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Diagnose why `C:\Users\Redwa\Desktop\debug1.rdc` and `debug2.rdc` show the ocean too dark after the CK3 1061 conservative convergence pass.

**Success Criteria:**
- Identify whether the darkness is caused by missing draw/bad resources, post-processing, or Ocean shader composition.
- Adjust only Ocean-local water energy parameters if the root cause is the copied CK3 color scale.

---

## Context & Background

**Previous Work:**
- `2026-06-24-ocean-conservative-energy-convergence.md`
- `2026-06-24-ocean-current-vs-ck3-1061-parameter-compare.md`

**Current State:**
- Ocean uses `RiverStrideLighting`, shared refraction capture, and CK3-style core water composition.
- Prior pass copied CK3 near-black water colors, `_WaterDiffuseMultiplier=0`, `_OceanSceneLightingScale=0.05`, `_OceanRefractionColorScale=0.085`, and `_WaterReflectionIntensity=0.025`.

**Why Now:**
- `debug1.rdc` showed that the full ocean surface became too dark in the current Stride lighting model.
- `debug2.rdc` confirmed that the first correction was still too dark and that the ocean animation speed was too high.

---

## What We Did

### 1. RenderDoc Pixel Diagnosis
**Files Inspected:** `C:\Users\Redwa\Desktop\debug1.rdc`, `C:\Users\Redwa\Desktop\debug2.rdc`

**Findings:**
- Capture is valid D3D11 with 64 draws and no HIGH severity log messages.
- EID 280 is the Ocean draw: indexed 6, output `ResourceId::4089`, PS binds environment, shadow, water, foam, and refraction textures.
- At ocean pixel `(1200,520)`, pixel history shows:
  - EID 204 terrain/scene output: `[2.9785, 1.8730, 1.0830, 1.0]`
  - EID 280 Ocean output: `[0.0590, 0.0930, 0.0886, 1.0]`
  - EID 939 swapchain output: `[0.0627, 0.1176, 0.1098, 0.0118]`

**Root Cause:**
- Ocean is not missing and not post-processed to black. The Ocean pass itself overwrites bright HDR scene color with a very dark opaque water result.
- CK3's near-black water colors, zero diffuse term, and `0.085` refraction scale do not transfer directly to this Stride HDR scene/lighting/tonemap.
- `debug2.rdc` confirmed the newer shader was loaded, but the same ocean pixel still landed at `[0.1176, 0.1867, 0.1814]` after Ocean overwrote pre-water terrain color `[2.8691, 1.8408, 1.0684]`.

### 2. Ocean-Only Energy Recalibration
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Rendering/Ocean/OceanMaterialSettings.cs`
- `Terrain/Assets/MainScene.sdscene`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `Terrain.Editor.Tests/RuntimeOceanAssetTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Implementation:**
- Kept CK3-compatible flow/gloss shape:
  - `_WaterFlowNormalScale = 0.025f`
  - `_WaterSpecular = 0.01f`
  - `_WaterGlossBase = 1.15f`
  - `_WaterGlossScale = 0.1f`
- Replaced CK3-copied color energy with Stride visual Ocean defaults:
  - `_WaterDiffuseMultiplier = 0.35f`
  - `_OceanSceneLightingScale = 0.5f`
  - `_OceanRefractionColorScale = 0.85f`
  - `_WaterReflectionIntensity = 0.16f`
  - `_OceanWaterColorTextureInfluence = 0.35f`
  - Shallow color `0.085, 0.18, 0.22`
  - Deep color `0.012, 0.045, 0.07`
- Changed diffuse from tiny fallback colors to `textureWaterColor * _WaterDiffuseMultiplier`.
- Added `_OceanWaveSpeedScale = 0.2f` and `_OceanFlowSpeedScale = 0.2f` so ambient normals, flow normals, and foam noise animate more slowly without changing CPU global time or River.

**Rationale:**
- This preserves the Ocean-only boundary and avoids province/FOW/flatmap/global lighting changes.
- Refraction is still scaled locally inside Ocean, but no longer crushed to 8.5% of scene color.
- Diffuse now participates in the current Stride scene lighting model without making shared River lighting brighter.
- Limiting water color texture influence prevents CK3's dark/murky texture from dominating the whole ocean color in this renderer.

---

## Decisions Made

### Decision 1: Do not mechanically copy CK3 color energy
**Context:** CK3's water shader runs inside a different strategy-map lighting/exposure stack.

**Decision:** Keep CK3 geometric/shape semantics where useful, but calibrate Ocean color energy for this Stride renderer.

**Trade-offs:**
- Less numeric parity with CK3 EID 1061 constants.
- Better visual behavior in the current HDR scene and tonemap.

---

## Problems Encountered & Solutions

### Problem 1: Ocean overwrites HDR scene with near-black color
**Symptom:** Ocean appears black in `debug1.rdc`.

**Root Cause:** The conservative CK3 copy made refraction, diffuse, scene lighting, and reflection all too low for this renderer.

**Solution:**
- Raise Ocean-only refraction/scene/reflection scales.
- Restore a low diffuse term from `textureWaterColor`.
- Use Stride-calibrated fallback water colors.
- Limit CK3 water color texture influence and add Ocean-only animation speed scales.

**Pattern for Future:**
- Use CK3 captures as semantic references, not as literal energy defaults, unless the lighting/exposure stack is also matched.

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`

### New Anti-Pattern Discovered
**New Anti-Pattern:** Copying CK3 water energy constants into Stride without matching lighting/exposure.
- What not to do: Do not copy near-black CK3 water colors, zero diffuse, and tiny refraction scale as a bundle.
- Why it is bad: The Ocean pass is opaque and can overwrite valid HDR terrain color with near-black water.
- Add warning to: Current session log and architecture notes.

---

## Testing

**Tests Updated:**
- Ocean shader text tests now lock Stride-calibrated energy defaults.
- Runtime scene/material tests now lock Stride-calibrated water colors.

**Manual Tests:**
- Fresh shader key generation: passed.
- `StrideCleanAsset`: passed.
- `StrideCompileAsset`: passed.
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`: passed.
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows`: passed after the `debug2` follow-up.
- Full solution/editor test path: blocked by Visual Studio PID 18512 and `Terrain.Editor.exe` PID 31912 locking `Bin\Editor\Debug\win-x64\Terrain.dll`.
- Editor tests: attempted, but build failed before test execution at MSB3027/MSB3021 due to the same locked DLL.
- Fresh RenderDoc capture still required to confirm the adjusted values visually.

---

## Next Session

### Immediate Next Steps
1. Close Visual Studio / running `Terrain.Editor.exe`, then rerun full solution build and editor tests.
2. Capture a new RenderDoc frame and confirm the same ocean pixel no longer lands near `[0.06,0.09,0.09]`.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `debug1.rdc` EID 280 Ocean draw was valid but too dark.
- Pixel `(1200,520)` proved Ocean overwrote terrain HDR `[2.98,1.87,1.08]` with `[0.059,0.093,0.089]`.
- Current fix is Ocean-only energy recalibration, not shared capture or global lighting changes.

**Gotchas for Next Session:**
- Do not revert shared refraction RGB passthrough.
- Do not add CK3 strategy-layer tokens.
- Do not tune River to fix Ocean.
