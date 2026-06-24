# Ocean final display response calibration
**Date**: 2026-06-24
**Session**: Ocean debug1 final-color match
**Status**: Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Match the user's screenshot final displayed Ocean color under the current Stride post chain, without mechanically copying CK3 1061 HDR raw values.

**Success Criteria:**
- Validate the formula first with RenderDoc hot replacement on `C:\Users\Redwa\Desktop\debug1.rdc`.
- Keep the fix Ocean-only.
- Do not modify shared refraction capture, River, global tonemap, scene light, province/FOW/flatmap paths, or `_WaterToSunDir`.

---

## Context & Background

**Previous Work:**
- `2026-06-24-ocean-lighting-model-ck3-hue.md`
- Earlier attempts showed that CK3 near-black raw parameters do not survive this project's different HDR/post chain.

**Current State:**
- Ocean already uses CK3-style refraction/flow/gloss structure and Ocean-local lighting scale.
- `debug1.rdc` still produced a final display color that was too dark.

---

## What We Did

### 1. RenderDoc hot-replace calibration
**Files Inspected:** `C:\Users\Redwa\Desktop\debug1.rdc`

**Baseline:**
- Ocean EID 280 at `(700,820)` raw output was approximately `[0.053, 0.124, 0.142]`.
- Final EID 1099 at the same point was approximately `[0.063, 0.161, 0.180]`.

**Hot-replace checks:**
- Constant raw `[0.13,0.21,0.24]` mapped through current post to the desired deep-water final range.
- Constant raw `[0.28,0.38,0.36]` mapped through current post near the desired shallow-water final range, but needed a stronger shallow response in the full shader.
- Full Ocean HLSL v3 hot replacement used:
  - deep raw target `[0.13,0.21,0.24]`
  - shallow raw target `[0.32,0.42,0.39]`
  - shallow depth divisor `14.0`
  - detail contrast `0.45`

**Result:**
- v3 final region means were about:
  - deep/open water: `[0.184..0.197, 0.291..0.303, 0.312..0.320]`
  - near shore: `[0.223, 0.321, 0.332]`
- The result was no longer constant flat color and retained wave/reflection variation.

### 2. Ocean shader implementation
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`

**Implementation:**
- Added Ocean-only display response parameters:
  - `_OceanDisplayDeepRawTarget = float3(0.13f, 0.21f, 0.24f)`
  - `_OceanDisplayShallowRawTarget = float3(0.32f, 0.42f, 0.39f)`
  - `_OceanDisplayShallowDepth = 14.0f`
  - `_OceanDisplayDetailContrast = 0.45f`
  - `_OceanDisplayResponseStrength = 1.0f`
- Added `ApplyOceanDisplayResponse(finalColor, baseRefractionDepth)`.
- Applied the response after lighting, refraction, and reflection composition, immediately before `streams.ColorTarget`.

**Rationale:**
- The current renderer's post chain brightens different raw ranges than CK3, so matching CK3 raw values is the wrong target.
- Using the Ocean raw output as a carrier for final display color keeps the compensation local while preserving high-frequency detail from the original lighting/refraction result.

### 3. Regression coverage
**Files Changed:**
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`

**Coverage Added:**
- Stage parameter declarations and defaults for all `_OceanDisplay*` values.
- `ApplyOceanDisplayResponse` helper presence.
- Depth blend from `baseRefractionDepth`.
- Luma/detail preservation formula.
- Final call site after full Ocean composition.

---

## Decisions Made

### Decision 1: Match final display color, not CK3 raw draw values
**Context:** The screenshot target is post-tonemap/final display. CK3's HDR raw draw values only make sense under CK3's full chain.

**Decision:** Calibrate a local Ocean display response against the current post chain.

**Trade-offs:**
- This is not full CK3 post-processing parity.
- It avoids changing shared capture, River, or global tonemap to solve an Ocean-only color problem.

### Decision 2: Use v3 constants instead of the initial `/8` shallow mask
**Context:** The initial formula was too weak in shallow/near-shore areas during full HLSL hot replacement.

**Decision:** Use shallow target `[0.32,0.42,0.39]` and depth divisor `14.0`.

**Trade-offs:**
- The source constants differ from the first draft formula, but they are backed by the requested RenderDoc hot-replace gate.

---

## Architecture Impact

### Documentation Updates
- Updated `docs/ARCHITECTURE_OVERVIEW.md`.
- Updated `docs/CURRENT_FEATURES.md`.

### Scope Guardrails
- Shared refraction capture remains RGB passthrough with alpha distance payload.
- River remains unchanged.
- Global tonemap and scene lighting remain unchanged.
- No province/FOW/flatmap/`_WaterToSunDir` paths were added.

---

## Testing

**RenderDoc Verification:**
- Completed hot-replace validation on `debug1.rdc` before changing `OceanSurface.sdsl`.

**Build/Asset Verification:**
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed with existing NuGet and nullable warnings.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed.

---

## Quick Reference for Future Claude

**What Changed:**
- Ocean now has a final display response helper calibrated from `debug1.rdc` hot replacement.
- The helper targets current final display color, not CK3 1061 raw HDR values.

**Gotchas:**
- Do not "fix" this by reapplying CK3 near-black water constants unless the post chain is also matched.
- Do not move this response into shared refraction capture or global tonemap.
- If future RenderDoc captures are still too dark/bright, adjust `_OceanDisplay*` parameters first, with another hot-replace gate.

