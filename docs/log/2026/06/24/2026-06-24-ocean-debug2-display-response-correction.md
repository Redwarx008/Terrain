# Ocean debug2 display response correction
**Date**: 2026-06-24
**Session**: Ocean debug2 vs CK3 follow-up
**Status**: Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Re-diagnose why `C:\Users\Redwa\Desktop\debug2.rdc` still looked completely different from CK3 after the previous display-response fix.

**Success Criteria:**
- Compare `debug2.rdc` against CK3 `save.rdc` with RenderDoc evidence.
- Hot-replace before changing SDSL.
- Keep the fix Ocean-only.

---

## Context & Background

**Previous Work:**
- `2026-06-24-ocean-final-display-response.md`

**Current State:**
- Ocean was using a constant raw-target display response:
  - deep `[0.13,0.21,0.24]`
  - shallow `[0.32,0.42,0.39]`
  - shallow depth `14.0`
  - detail contrast `0.45`

**Why Now:**
- User feedback: `debug2.rdc` and CK3 still looked completely different.

---

## What We Did

### 1. RenderDoc comparison
**Files Inspected:**
- `C:\Users\Redwa\Desktop\debug2.rdc`
- `C:\Users\Redwa\Desktop\save.rdc`

**Current debug2 with constant target response:**
- Ocean raw at open-water points was about `[0.165..0.167,0.282..0.283,0.301]`.
- Final post output at those points was about `[0.204..0.212,0.310..0.314,0.325]`.
- Open water was visually too uniform and too bright/green-blue.

**CK3 save.rdc:**
- CK3 Ocean raw EID 1061 open-water samples were still very dark, around `[0.010..0.014,0.020..0.026,0.025..0.033]`.
- CK3 final display samples after post were around `[0.141..0.169,0.212..0.247,0.247..0.286]`.

**Trace finding:**
- At debug2 `(700,820)`, the real Ocean composition before display response was about `[0.0578,0.1272,0.1436]`.
- The constant-target response then replaced it with `[0.1655,0.2830,0.3013]`.
- `shallowMask` was about `0.319` even for open water because the old divisor was `14.0`, so open water was partially receiving shallow color.

### 2. Hot-replace probes

**Probe 1: Disable display response**
- Raw returned to about `[0.052..0.058,0.116..0.127,0.133..0.144]`.
- Final returned to about `[0.051..0.063,0.149..0.161,0.169..0.180]`.
- This confirmed some Ocean-only lift is still needed.

**Probe 2: Lower constant target and smaller shallow depth**
- Final open water moved to about `[0.122,0.23,0.263]`.
- Color was closer but still visually too target-driven and banded.

**Probe 3: Preserve original finalColor with gain+bias**
- Formula:
  - deep: `finalColor * 1.15 + [0.05,0.045,0.055]`
  - shallow: `finalColor * 0.8 + [0.16,0.22,0.20]`
  - shallow depth divisor: `6.0`
- Final open-water points:
  - `(700,820)` -> `[0.149,0.231,0.259]`
  - `(1200,520)` -> `[0.137,0.220,0.247]`
  - `(1400,800)` -> `[0.141,0.224,0.251]`
- This matches the CK3 deep-water final display range more closely while preserving the original Ocean variation better than the constant target response.

### 3. Source update
**Files Changed:**
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

**Implementation:**
- Removed flat `_OceanDisplayDeepRawTarget`, `_OceanDisplayShallowRawTarget`, and `_OceanDisplayDetailContrast` parameters.
- Added:
  - `_OceanDisplayDeepGain = 1.15f`
  - `_OceanDisplayDeepBias = float3(0.05f,0.045f,0.055f)`
  - `_OceanDisplayShallowGain = 0.8f`
  - `_OceanDisplayShallowBias = float3(0.16f,0.22f,0.20f)`
  - `_OceanDisplayShallowDepth = 6.0f`
- `ApplyOceanDisplayResponse` now blends deep/shallow gain+bias responses using base refraction depth, then strength-blends against original `finalColor`.

---

## Decisions Made

### Decision 1: Do not use constant raw targets for open water
**Context:** Constant targets matched a few debug1 points but flattened debug2 open water and pushed it too bright.

**Decision:** Preserve original Ocean composition with gain+bias instead of replacing it.

**Trade-offs:**
- This is still a local compensation for the current post chain, not CK3 post-processing parity.
- It gives later tuning more stable controls: gain/bias/depth instead of a single target color that can erase detail.

---

## Architecture Impact

### Documentation Updates
- Updated `docs/ARCHITECTURE_OVERVIEW.md`.
- Updated `docs/CURRENT_FEATURES.md`.

### Scope Guardrails
- No shared refraction capture change.
- No River change.
- No global tonemap or scene light change.
- No province/FOW/flatmap/`_WaterToSunDir` path.

---

## Testing

**RenderDoc Verification:**
- Hot-replaced `debug2.rdc` before source edits.
- Restored RenderDoc shader replacements before changing source.

**Build/Asset Verification:**
- `dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed.
- `dotnet build Terrain\Terrain.csproj --no-restore /p:Configuration=Debug /p:TargetFramework=net10.0-windows` passed with existing NuGet and nullable warnings.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- The problem was not simply "water too dark" anymore.
- The constant target response made open water too uniform and too bright.
- The current formula is intentionally a gain+bias over real Ocean `finalColor`.

**Gotchas:**
- Do not reintroduce `_OceanDisplayDeepRawTarget` / `_OceanDisplayShallowRawTarget` unless another RenderDoc hot-replace proves it.
- `debug2.rdc` also differs from CK3 at the whole-scene level: terrain material/camera/UI state are not equivalent, so Ocean-only matching cannot make the entire frame identical.
