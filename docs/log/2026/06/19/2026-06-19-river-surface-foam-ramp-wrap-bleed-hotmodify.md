# River Surface Foam Ramp Wrap Bleed Hotmodify
**Date**: 2026-06-19
**Session**: 14
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续分析更新后的 `C:\Users\Redwa\Desktop\debug.rdc`，确认河流水面仍黑且有大块白斑的 surface 根因，并在改 SDSL 前优先用 RenderDoc 热修改验证。

**Success Criteria:**
- 区分 bottom/refraction、surface `CalcWater`、foam、shadow/cloud/fog post 的责任边界。
- 对比 CK3 shader 源码和 `ck3-river.rdc` 采样行为。
- 只落地 RenderDoc 已验证有效的 shader delta。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-surface-wrapper-shadow-tint-hotmodify.md](./2026-06-19-river-surface-wrapper-shadow-tint-hotmodify.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- current capture: `C:\Users\Redwa\Desktop\debug.rdc`
- CK3 capture: `C:\Users\Redwa\Desktop\ck3-river.rdc`
- current river surface draw: `event 315`
- CK3 river surface draw: `event 466`

---

## What We Did

### 1. Re-isolated current surface output
**Files Changed:** none

**Implementation:**
- Used RenderDoc MCP on current `event 315`.
- Confirmed direct refraction source was clean, while pixel history showed black/white pixels were written by surface shader output itself.

**Result:**
- Black point `(930,650)` original surface shaderOut: about `[0.0584,0.0452,0.0362]`.
- White point `(720,700)` original surface shaderOut: about `[0.6625,0.5626,0.4773]`.
- Pure base refraction + see-through remained dark at the white point, so white was not from bottom/refraction.

### 2. Corrected replacement input semantics
**Files Changed:** none

**Implementation:**
- Rechecked `RiverVertexStreams.sdsl` and shader reflection.
- Correct mapping is:
  - `RiverUV : TEXCOORD1`
  - `RiverWidth : TEXCOORD4`
  - `RiverDistanceToMain : TEXCOORD5`
  - `RiverTransparency : TEXCOORD0`
  - `RiverNormal : TEXCOORD3`

**Result:**
- Earlier probes that declared `TEXCOORD1` as a scalar had read the wrong river UV values and were discarded.
- Correct probes showed `waterFade≈0.13` at both black and white sample points.

### 3. Identified foam as the white-block source
**Files Changed:** none during hot test

**Implementation:**
- Replaced current surface PS with a correct `CalcFoamFactor` diagnostic.

**Result:**
- Black point `(930,650)` foam: `0.0`.
- White point `(720,700)` foam: `0.403`.
- Therefore the high white output was driven by foam, not shadow/fog, normal lighting, or blend state.

### 4. Compared CK3 foam ramp behavior
**Files Changed:** none during hot test

**Implementation:**
- Sampled current `FoamRampTexture(t4)` and CK3 `FoamRampTexture(t8)` at `u=0,y=0.5`.
- Verified DDS file hash for local `foam-ramp.dds` matches CK3 `foam_ramp.dds`.

**Result:**
- current `u=0,y=0.5`: `[0.323,0.325,0.290]`
- CK3 `u=0,y=0.5`: `[0,0.0003,0]`
- Local DDS content was identical; difference came from current sampling with wrap + linear at the texture edge.

### 5. Hot-validated the fix and ported it to SDSL
**Files Changed:**
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- Hot probe changed foam ramp lookup to `max(u, 0.5/256)`.
- SDSL now clamps ramp U to `[0.5/256, 1-0.5/256]` and uses `SampleLevel(..., 0)`.
- SDSL also passes `InputWorldSpacePos.xz` into `CalcFoamFactor`, matching CK3 `Input.WorldSpacePos.xz`, instead of `mapUnitXZ`.

**Result:**
- White point foam dropped from `0.403` to `0.0` in the hot probe.

---

## Decisions Made

### Decision 1: Fix foam ramp lookup in shader, not the DDS
**Context:** Local `foam-ramp.dds` hash matches CK3, but GPU sampling differs at `u=0`.
**Decision:** Keep the asset, change sampling to lod0 + half-texel U clamp.
**Rationale:** The error is wrap edge bleed, not bad texture content or asset registration.

### Decision 2: Align foam world-coordinate input with CK3
**Context:** CK3 passes `Input.WorldSpacePos.xz` into `CalcFoamFactor`; current used `mapUnitXZ`.
**Decision:** Pass `InputWorldSpacePos.xz`.
**Rationale:** This removes a real shader-source inequivalence and avoids wrong foam/noise scale.

---

## What Worked ✅

1. **Direct ramp sampling replacement**
   - It separated asset content from sampler behavior: identical DDS, different `u=0` GPU result.

2. **Pixel history over visual guesses**
   - It proved white blocks were shaderOut from `event 315`, not post-blend artifacts.

---

## What Didn't Work ❌

1. **Initial replacement input struct**
   - Treating `TEXCOORD1` as a scalar shifted later inputs and made early foam/waterFade values invalid.
   - Correct replacement shaders must mirror `RiverVertexStreams.sdsl` and shader reflection.

2. **Assuming `FlowFoamMask=0` means foam output must be 0**
   - With wrap sampling, `FoamRampTexture(u=0)` was not black, so dotting foam texture with the ramp still produced visible foam.

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
  - Existing NuGet and C# warnings remain.

---

## Next Session

### Immediate Next Steps
1. Capture a fresh `debug.rdc` and confirm current surface disasm contains `FoamRampTexture.SampleLevel` and the half-texel ramp clamp.
2. Re-check river surface output around the prior white point; the foam-driven white block should be gone.
3. Continue the separate remaining mismatch: current pre-bottom/refraction source is still a bright transparent-stage HDR target, unlike CK3's darker `JominiRefraction` payload.

### Gotchas
- Do not re-copy `foam-ramp.dds`; hash already matches CK3.
- Do not diagnose foam from `FoamMap` alone; the actual failure was ramp sampler wrap bleed.
- RenderDoc replacement shaders for river surface must use `RiverUV:TEXCOORD1`, `RiverWidth:TEXCOORD4`, and `RiverDistanceToMain:TEXCOORD5`.

---

## Session Statistics

**Files Changed:** 6 tracked files plus this log
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Root cause of white surface blocks: `FoamRampTexture` sampled with wrap at `u=0`, causing left/right edge bleed.
- Fix: `foamRampU = clamp(foamFactor * FlowFoamMask, 0.5/256, 1-0.5/256)` and `SampleLevel(..., 0)`.
- CK3 ramp `u=0` is black; current before fix was `[0.323,0.325,0.290]`.
