# River Surface Wrapper Shadow Tint Hotmodify
**Date**: 2026-06-19
**Session**: 13
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续用 RenderDoc MCP 热修改定位 current 河流水面比 CK3 明显偏亮的原因，并在验证后再改 SDSL。

**Success Criteria:**
- 计算 CK3 comparable bank 像素的真实 refraction depth。
- 区分 `CalcWater` 主体、FogOfWar 和完整 surface wrapper 对最终颜色的影响。
- 只把热修改验证有效的方向接回 `RiverSurface.sdsl`。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-surface-refraction-depth-hotmodify-analysis.md](./2026-06-19-river-surface-refraction-depth-hotmodify-analysis.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- current capture: `C:\Users\Redwa\Desktop\debug.rdc`
- CK3 capture: `C:\Users\Redwa\Desktop\ck3-river.rdc`
- current surface event remains `305`; CK3 surface event remains `466`.

---

## What We Did

### 1. CK3 refraction physical depth dump
**Files Changed:** none

**Implementation:**
- Used RenderDoc MCP shader replacement on CK3 `event 466`.
- Replacement scanned bound cbuffers for the camera candidate and applied CK3 `MaxHeight=50` `DecompressWorldSpace` clamp.

**Result:**
- CK3 `(110,738)`:
  - `world ~= (7575.02, 4.27, 2967.33)`
  - `camera ~= (7635.48, 53.62, 2928.35)`
  - `RefractionTexture.a = 82.0781`
  - `clampedSurfaceDistance = 80.8341`
  - `RefractionDepth ~= 0.704`
- current comparable `(30,768)` remained:
  - `RefractionDepth ~= 0.230`
  - `see-through output ~= [0.292, 0.212, 0.143]`

**Rationale:**
- current refraction payload is physically shallower than CK3, but this alone did not explain CK3 final `[0.022,0.028,0.030]`.

### 2. Separated CK3 `CalcWater` from full surface wrapper
**Files Changed:** none

**Implementation:**
- Replaced CK3 surface PS with a probe that computed base refraction see-through.
- Separately sampled `FogOfWarAlpha_Texture` at the same CK3 pixel.

**Result:**
- CK3 base see-through at `(110,738)` was about `[0.098, 0.095, 0.072]`.
- CK3 full surface output was `[0.0223, 0.0280, 0.0305]`.
- `FogOfWarAlpha` sample was `[1,1,0,1]`; FOW was not the darkening source for this pixel.

**Conclusion:**
- The remaining darkening comes from `river_surface.shader` wrapper, mainly terrain shadow tint / cloud tint, not from FOW.

### 3. Hot-validated current shadow tint energy gate
**Files Changed:** none during hot test

**Implementation:**
- Reopened current capture and hot-replaced current `event 305` surface output with CK3 shadow tint target color `[0.023,0.023,0.033]`.

**Result:**
- current `(30,768)` output became `[0.0230,0.0230,0.0330]`, matching CK3 energy range.

**Conclusion:**
- Reconnecting surface post RGB wrapper is a valid fix direction.

### 4. Reconnected surface wrapper in SDSL
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- `ApplySurfacePostProcessing` now applies:
  - `GetCloudShadowMask`
  - `ApplyTerrainShadowTintWithClouds`
  - CK3 cloudy color blend toward `float3(0.0, 0.01, 0.02)`
  - `ApplyMapDistanceFogWithoutFoW`
- Kept FOW deleted:
  - no `FogOfWarAlphaTexture`
  - no FOW sampler
  - no `_HasFogOfWarAlphaTexture`
  - no `ApplyFogOfWar`
- Updated text tests to lock this split.

---

## Decisions Made

### Decision 1: Re-enable RGB wrapper, keep FOW removed
**Context:** CK3 FOW sampled as visible, but wrapper still darkened water.
**Decision:** Apply shadow/cloud/fog post RGB in `RiverSurface.sdsl`; do not reintroduce strategy-layer FOW.
**Rationale:** Hot tests showed shadow-tint color alone moves current into CK3 energy range, while FOW was not the culprit.

### Decision 2: Do not tune water constants for this mismatch
**Context:** `_WaterSeeThroughDensity` and related constants already match CK3 settings.
**Decision:** Stop trying to solve this by changing water color or see-through constants.
**Rationale:** current surface output was equal to see-through output; CK3 final differed because of wrapper stages.

---

## What Worked ✅

1. **MCP replacement cbuffer scan**
   - Found CK3 camera and current camera without relying on unavailable direct cbuffer dumps.

2. **Splitting base see-through from full wrapper**
   - Showed `CalcWater` was not the final color boundary; `river_surface.shader` wrapper materially changes RGB.

---

## What Didn't Work ❌

1. **Assuming FOW removal implies wrapper removal is safe**
   - FOW was not darkening this pixel, but terrain shadow/cloud tint was.

2. **Comparing `RefractionTexture.a` without clamp-aware decompression**
   - CK3 alpha is measured from a camera position clamped to height `50`, not raw camera distance.

---

## Verification

- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`
  - Passed.
  - Existing NuGet vulnerability warnings remain.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - Passed.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - Passed.
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
  - Passed, `913 succeeded`, `0 failed`.
  - Existing shader loop-unroll warning remains.
- `dotnet build Terrain.Editor\Terrain.Editor.csproj -c Debug`
  - Passed.
  - Existing NuGet vulnerability warnings remain.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh `debug.rdc` after the SDSL rebuild and verify `event 305` disasm contains the shadow/cloud/fog RGB wrapper.
2. Compare current bank `(30,768)` or newly selected equivalent point against CK3 after the wrapper is active.
3. Continue investigating bottom/refraction payload shallowness separately; CK3 physical refraction depth was about `0.704` versus current `0.230`.

### Gotchas
- Do not reintroduce `FogOfWarAlphaTexture`; FOW was explicitly ruled out for this pixel.
- Do not treat `CalcWater` output as final surface color; CK3 wrapper can change RGB by a large factor.
- Do not compare raw `RefractionTexture.a` across captures without applying the `MaxHeight=50` camera clamp.

---

## Session Statistics

**Files Changed:** 5 tracked files plus this log
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- The root for the latest bright-bank gap is missing RGB post wrapper, not FOW.
- `RiverSurface.sdsl` now reconnects terrain shadow tint, cloud tint, and map distance fog in `ApplySurfacePostProcessing`.
- FOW remains removed by design.
- Fresh RenderDoc verification is still required after the user produces a new capture.
