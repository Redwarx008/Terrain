# Ocean lighting-model CK3 hue calibration
**Date**: 2026-06-24
**Session**: Ocean debug2 lighting follow-up
**Status**: Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Move the Ocean fix away from final color correction and toward a lighting-model correction that keeps the ocean hue closer to CK3 without blindly copying CK3 constants.

**Success Criteria:**
- Use `debug2.rdc` and CK3 `save.rdc` EID 1061 as evidence.
- Keep changes Ocean-only.
- Do not change shared refraction capture, River lighting, global scene light, province/FOW/flatmap paths, or `_WaterToSunDir`.

---

## Context & Background

**Previous Work:**
- `2026-06-24-ocean-debug1-black-fix.md`

**Current State:**
- Ocean already uses shared refraction, CK3-style water normals/refraction/fade, and `RiverStrideLighting`.
- The earlier CK3 constant copy produced black water.
- The first Stride-calibrated correction avoided black water but pushed the ocean into an overly bright cyan range.

**Why Now:**
- User feedback: do not solve this as a mechanical CK3 parameter copy or final color correction; inspect from the lighting model.

---

## What We Did

### 1. RenderDoc evidence
**Files Inspected:** `C:\Users\Redwa\Desktop\debug2.rdc`, `C:\Users\Redwa\Desktop\save.rdc`

**Findings:**
- Current `debug2.rdc` Ocean draw EID 280 at `(1200,520)` writes approximately `[0.421, 0.758, 0.814]`, after terrain wrote about `[2.89, 1.85, 1.08]`.
- CK3 `save.rdc` EID 1061 open-water sample `(800,1500)` writes about `[0.014, 0.026, 0.033]`.
- CK3 1061 linear RT values are very dark, so literal numeric matching would return to black water in this renderer.
- The current local export is bright cyan; the issue is no longer simply missing brightness.

### 2. Lighting-model correction
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- `Terrain/Rendering/Ocean/OceanMaterialSettings.cs`
- `Terrain/Assets/MainScene.sdscene`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `Terrain.Editor.Tests/RuntimeOceanAssetTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Implementation:**
- Added Ocean-local `ComputeOceanLighting(...)`.
- Kept reusing `RiverStrideLighting` helper functions, but stopped calling `RiverStrideComputeLighting(...)` directly for Ocean because that helper only scales environment IBL, not direct sun.
- Direct diffuse/specular and environment diffuse/specular now all use `_OceanSceneLightingScale`.
- Updated calibrated Ocean defaults:
  - `_WaterDiffuseMultiplier = 0.20f`
  - `_OceanSceneLightingScale = 0.18f`
  - `_OceanRefractionColorScale = 0.30f`
  - `_WaterReflectionIntensity = 0.10f`
  - `_OceanWaterColorTextureInfluence = 0.20f`
  - Shallow `[0.035, 0.09, 0.11]`
  - Deep `[0.006, 0.024, 0.034]`

**Rationale:**
- The previous parameter named `_OceanSceneLightingScale` did not scale all scene lighting in practice.
- Scene sun intensity is about `20`; without a direct-light scale, even low diffuse colors can become bright cyan.
- The new values target CK3 hue and water-lighting balance without copying CK3's near-black material colors or tiny refraction scale as a bundle.

---

## Decisions Made

### Decision 1: Fix Ocean direct lighting rather than final color
**Context:** User explicitly asked to approach this from the lighting model.

**Decision:** Add an Ocean-only lighting wrapper that scales direct and environment contributions consistently.

**Trade-offs:**
- Ocean no longer calls `RiverStrideComputeLighting(...)` directly.
- River keeps the shared helper unchanged.
- Text tests now lock the composition helpers instead of the direct call.

---

## Problems Encountered & Solutions

### Problem 1: Ocean scene lighting scale did not scale direct sun
**Symptom:** Ocean could become high-brightness cyan even when the pass was intended to be energy-scaled.

**Root Cause:** `RiverStrideComputeLighting(..., environmentIntensityScale)` applies the scale only to environment diffuse/specular. Direct diffuse/specular still use `_SceneSunColor` at full strength.

**Solution:**
- In `OceanSurface`, compute `scaledLightColorNdotL = RiverStrideGetMainLightColorNdotL(...) * _OceanSceneLightingScale`.
- Use that scaled light for direct diffuse/specular.
- Continue passing `_OceanSceneLightingScale` to environment diffuse/specular helpers.

**Pattern for Future:**
- Do not assume a parameter name reflects all energy paths. Inspect the actual helper implementation before using it as a global lighting scale.

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`

### New Anti-Pattern Discovered
**New Anti-Pattern:** Treating an environment-only scale as a scene-lighting scale.
- What not to do: Do not pass Ocean energy through `RiverStrideComputeLighting` and assume direct sun is scaled.
- Why it is bad: Direct sun can still use scene intensity `20` and push low water colors into bright cyan.
- Add warning to: Architecture notes and this session log.

---

## Testing

**Automated/Build Verification:**
- Shader key generation passed.
- Asset compile/build verification is still pending at the time this log was created.

**Manual/GPU Verification:**
- RenderDoc evidence was collected from existing captures.
- A fresh post-change RenderDoc capture is still required for visual confirmation.

---

## Next Session

### Immediate Next Steps
1. Run `StrideCleanAsset`, `StrideCompileAsset`, project/solution build, and editor tests.
2. Capture a fresh RenderDoc frame after the shader change.
3. Compare the Ocean draw output and screenshot against CK3 hue, not raw CK3 1061 linear luminance.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- The key model bug was direct sun bypassing `_OceanSceneLightingScale`.
- `ComputeOceanLighting(...)` is Ocean-only and intentionally does not modify River.
- CK3 1061 linear RT values are very dark; matching them numerically is not the target.

**Gotchas for Next Session:**
- Do not re-copy CK3 near-black material colors unless the exposure/tonemap chain is also matched.
- Do not change shared refraction capture or River lighting to fix Ocean.
- Do not add province/FOW/flatmap/`_WaterToSunDir` paths.
