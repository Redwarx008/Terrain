# Ocean Current vs CK3 EID 1061 Parameter Compare
**Date**: 2026-06-24
**Session**: Ocean RenderDoc parameter comparison
**Status**: Complete
**Priority**: High

---

## Session Goal

Compare the current Ocean draw in `C:\Users\Redwa\Desktop\debug.rdc` against CK3 `C:\Users\Redwa\Desktop\save.rdc` EID 1061, with emphasis on per-parameter GPU state rather than source-level assumptions.

---

## Context & Background

Previous work had already moved Ocean closer to CK3 core water:
- shared refraction RGB passthrough instead of local compression;
- CK3-style four-phase flow normal interpolation;
- selected water DDS textures loaded as sRGB;
- conservative lighting changes.

Related sessions:
- `docs/log/2026/06/24/2026-06-24-ocean-ck3-renderdoc-compare.md`
- `docs/log/2026/06/24/2026-06-24-ocean-ck3-flow-refraction-fixes.md`

---

## What We Did

### 1. Opened both captures

- `debug.rdc`: D3D11, 76 draws, 90 events, no HIGH log messages.
- `save.rdc`: D3D11, 673 draws, 690 events, no HIGH log messages.

Full diff still cannot align useful draws because the frame structures are substantially different.

### 2. Identified target draws

Current Ocean:
- Event `280`
- `DrawIndexed`, 6 indices
- Output `ResourceId::4089`, `R16G16B16A16_FLOAT`, `1672x996`
- Depth `ResourceId::340`, `D24S8`, `1672x996`
- Shared refraction source `ResourceId::7845`, produced at EID `246`, `836x498`

CK3 target:
- Event `1061`
- `Draw`, 4 vertices
- Output `ResourceId::29905`, `R16G16B16A16_FLOAT`, `3584x2016`
- Depth `ResourceId::29908`, `D24S8`, `3584x2016`
- Refraction source `ResourceId::29913`, `1792x1008`

### 3. Compared output and refraction ranges

Current:
- Refraction `7845`: RGB max `[5.0586, 3.2539, 1.9170]`, alpha range `0.00029..98.875`
- Ocean output `4089`: RGB max `[5.6094, 5.6406, 5.2852]`, alpha `1`
- Center pixel `(836,498)`: `[0.3315, 1.3584, 1.9639, 1]`

CK3:
- Refraction `29913`: RGB max `[0.3872, 0.4316, 0.3914]`, alpha range `53.0938..322.25`
- Ocean output `29905`: RGB max `[0.5869, 0.4985, 0.5156]`, alpha `0..1`
- Center pixel `(1792,1008)`: picked RT `[0.0718, 0.0613, 0.0409, 1]`; shader summary output `[0.0741, 0.0624, 0.0417, 0]`

The current refraction source is still roughly an order of magnitude brighter than CK3 in RGB, even after removing the local compression path. The input scene color is the larger issue now.

### 4. Compared shader structure

Aligned:
- Current Ocean PS contains `_WaterFlowMapSize * 1.5`.
- Current Ocean PS uses derivative-aware `sample_d` for flow normal.
- Current Ocean PS separates base and offset refraction.
- Current Ocean PS uses unfiltered alpha payload `Texture2D.Load`.
- Current Ocean PS uses CK3-style wave scales, rotations, speeds, refraction shore mask, water fade, foam and see-through defaults for many scalar fields.

Still different:
- Current Ocean uses `RiverStrideLighting` scene sun, shadow and environment IBL.
- CK3 EID 1061 uses map-water constants: `SunDiffuse`, `SunIntensity`, `_WaterToSunDir`, ambient cube faces, `_WaterSpecularFactor`, `_WaterCubemapIntensity`, and gloss exponent logic.
- Current Ocean omits height/province/border/FOW/fog/flatmap strategy-layer code that CK3 runs after core water.

### 5. Compared key constant parameters

Largest numeric deltas:
- `_WaterFlowNormalScale`: current `8.0`, CK3 `0.025`
- `_WaterDiffuseMultiplier`: current `1.0`, CK3 `0.0`
- `_WaterSpecular`: current `0.05`, CK3 `1.0`
- `_WaterGlossBase`: current `0.7`, CK3 `1.15`
- `_WaterGlossScale`: current `1.0`, CK3 `0.1`
- reflection/cubemap: current `_WaterReflectionIntensity=1.0` and `_EnvironmentIntensity=20`; CK3 `_WaterCubemapIntensity=0.5`, `CubemapIntensity=1`
- sun: current scene color `[20,17.3568,15.0970]`; CK3 `SunDiffuse=[1,0.8364,0.6994]`, `SunIntensity=7`
- water colors: current `Shallow=[0.08,0.32,0.42]`, `Deep=[0.01,0.08,0.16]`; CK3 `_WaterColorShallow=[0.0055,0.0078,0.0121]`, `_WaterColorDeep=[0.000138,0.000197,0.000226]`
- map size: current `[18431,9215]`, CK3 `[8192,4096]`
- water height: current `12.1`, CK3 `3.8`
- camera height: current `562.5`, CK3 `64.28`

---

## Decisions Made

No code or architecture decisions were made. This session only gathered RenderDoc evidence.

---

## What Worked

1. Reading constant buffers through RenderDoc MCP exposed the actual mismatch more clearly than source inspection.
2. Texture statistics separated two issues: the current shader path is closer to CK3, but the current refraction input and lighting parameters are still not target-equivalent.

---

## What Didn't Work

1. Full-frame diff remains ineffective because the captures have different frame structures.
2. Treating "core water code is close" as enough would be misleading; the GPU constants show major remaining differences.

---

## Next Session

Immediate next steps:
1. Fix the most direct parameter mismatch first: bind Ocean `_WaterFlowNormalScale` to CK3-equivalent `0.025` rather than the current `8.0`.
2. Decide whether Ocean should use CK3 map-water lighting constants for parity mode instead of `RiverStrideLighting` scene IBL/direct lighting.
3. Investigate why current shared refraction input RGB reaches `[5.06,3.25,1.92]` while CK3 EID 1061 refraction is below `[0.39,0.44,0.40]`; this may require matching pre-water scene exposure/tone stage, not only Ocean shader code.
4. If visual parity with CK3 EID 1061 is required, explicitly choose whether to port or approximate province/border/FOW/fog/flatmap branches.

---

## Session Statistics

Files changed: 1 documentation log.
Code files changed: 0.
Commits: 0.

---

## Quick Reference

Key conclusion:
- Current Ocean is structurally closer to CK3 than the previous capture, but it is still much brighter because input refraction RGB and water lighting constants are not CK3-equivalent.

Most suspicious parameter:
- `_WaterFlowNormalScale` current `8.0` vs CK3 `0.025`.

Most suspicious energy source:
- Shared refraction RT `ResourceId::7845` max RGB `[5.0586,3.2539,1.9170]` vs CK3 `ResourceId::29913` max RGB `[0.3872,0.4316,0.3914]`.
