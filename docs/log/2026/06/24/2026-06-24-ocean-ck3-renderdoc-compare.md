# Ocean CK3 RenderDoc Compare
**Date**: 2026-06-24
**Session**: Ocean CK3 parity inspection
**Status**: Complete
**Priority**: High

---

## Session Goal

Compare current Ocean rendering in `C:\Users\Redwa\Desktop\debug.rdc` against CK3 water draw `C:\Users\Redwa\Desktop\save.rdc` EID 1061, focusing on draw state, shader code, resource bindings, and parameter/formula differences.

---

## Context & Background

Previous water renderer work connected Ocean and River to `CustomForwardRenderer` with shared refraction capture. Visual parity with CK3 still looked poor, so this session used RenderDoc evidence rather than TDD.

Related docs:
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/2026/06/24/2026-06-24-water-renderer-review-fixes.md`
- `docs/log/2026/06/24/2026-06-24-water-renderer-review-fixes-2.md`

---

## What We Did

### 1. Opened and Compared Captures

Used `renderdoc-mcp` and `renderdoc-cli` to inspect both captures.

Key frame facts:
- `debug.rdc`: D3D11, 64 draw calls, 271 resources.
- `save.rdc`: D3D11, 673 draw calls, 793 resources.
- Full-frame diff diverges at counts, so draw-to-draw comparison is required.

### 2. Identified Target Draws

CK3 target:
- Event: `1061`
- Draw: non-indexed draw, 4 vertices
- Output: `ResourceId::29905`, `R16G16B16A16_FLOAT`, `3584x2016`
- Depth: `ResourceId::29908`, `D24S8`, `3584x2016`
- Refraction source: `ResourceId::29913`, `R16G16B16A16_FLOAT`, `1792x1008`

Current target:
- Ocean draw event: `280`
- Draw: indexed quad, 6 indices
- Output: `ResourceId::4089`, `R16G16B16A16_FLOAT`, `1672x996`
- Depth: `ResourceId::340`, `D24S8`, `1672x996`
- Refraction source: `ResourceId::7856`, `R16G16B16A16_FLOAT`, `836x498`
- Shared refraction capture draw event: `246`

### 3. Compared Refraction and Output Ranges

CK3:
- Refraction `29913` at EID 1061:
  - Min RGB/A: `0.00194931, 0.00169468, 0.00133038, 53.0938`
  - Max RGB/A: `0.387207, 0.431641, 0.391357, 322.25`
- Output `29905` at EID 1061:
  - Min RGB/A: `0.00583267, 0.00927734, 0.00299263, 0`
  - Max RGB/A: `0.586914, 0.498535, 0.515625, 1`

Current:
- Refraction `7856` at EID 280:
  - Min RGB/A: `0.000157714, 0.000132084, 0.000111938, 36.9062`
  - Max RGB/A: `1.25195, 1.15527, 1.02246, 141.125`
- Ocean output `4089` at EID 280:
  - Min RGB/A: `0.000106633, 0.0000808835, 0.0000649691, 1`
  - Max RGB/A: `5.23828, 3.42969, 2.2832, 1`

This is the largest observed difference: current refraction input and final Ocean output are far brighter than CK3.

### 4. Shader and Binding Comparison

CK3 EID 1061 PS binds:
- Height lookup and packed height textures.
- Province color indirection/color textures.
- Border distance field and pattern texture.
- Water color, ambient normal, flow map, flow normal.
- Reflection cubemap.
- Foam, foam ramp, foam map, foam noise.
- Refraction texture.
- Fog-of-war alpha and flatmap texture.

Current EID 280 PS binds:
- Environment cubemap and scene shadow atlas.
- Water color, ambient normal, flow map, flow normal.
- Foam, foam ramp, foam map, foam noise.
- Refraction texture.

The current shader intentionally omits CK3 strategy-layer inputs: height/province/border/FOW/flatmap. That omission is no longer a minor wrapper issue; CK3 EID 1061 still executes those branches after core water.

### 5. Code Comparison Notes

Current `OceanSurface.sdsl` implements much of the CK3 core water path:
- Shared refraction capture.
- Unfiltered alpha payload via `Texture2D.Load`.
- Base and offset refraction separation.
- See-through attenuation.
- Water fade.
- Three ambient normal layers.
- Flow normal sampling.
- Foam ramp half-texel clamp.
- Fresnel and environment reflection.

Non-equivalent areas found:
- Current shared capture compresses scene color with Reinhard-like `color / (1 + color) * 1.5`; CK3 EID 1061 reads a darker refraction input with max RGB around `0.43`.
- Current output energy is much higher, with max RGB above `5`.
- Current flow normal code samples four flowmap cells but uses local `Load` + normalized flow vectors; CK3 disasm uses `_WaterFlowMapSize * 1.5`, rounded cell coordinates, cubic/diamond interpolation weights, and derivative-aware `sample_d` for flow normals.
- Current water lighting uses `RiverStrideLighting` GGX/direct/IBL. CK3 EID 1061 uses map water lighting constants such as `SunDiffuse`, `SunIntensity`, `_WaterToSunDir`, ambient cube faces, `_WaterSpecularFactor`, `_WaterCubemapIntensity`, and gloss exponent logic.
- Current Ocean loads all water DDS with `loadAsSrgb:false`; CK3 resource table shows several water/map textures as `B8G8R8A8_SRGB` or `BC*_SRGB`.
- Current Ocean output alpha is always `1.0`; CK3 target has alpha min `0`, max `1`, because later map/fog/flatmap paths affect alpha/output.

---

## Decisions Made

No code or architecture decisions were made. This was an evidence-gathering session.

---

## What Worked

1. Draw-to-draw RenderDoc comparison was more useful than full-frame diff because the captures have different draw/resource counts.
2. Resource usage history identified CK3 `ResourceId::29913` as the refraction source for EID 1061.
3. Texture statistics exposed the major brightness mismatch before any shader speculation.

---

## What Didn't Work

1. Full-frame diff did not align useful draws because the frame structures are too different.
2. MCP snapshot export did not include constant buffer values, so parameter values were inferred from shader defaults, binding names, disassembly immediates, and source code.

---

## Next Session

Immediate next steps:
1. Fix refraction capture energy first. Current capture RGB max `1.25` and output max `5.24` are far outside CK3's `0.43` and `0.59` ranges.
2. Port CK3 ocean flow normal interpolation more literally, especially `_WaterFlowMapSize * 1.5`, rounded cell sampling, derivative-aware flow-normal sampling, and interpolation weights.
3. Decide whether Ocean should intentionally include CK3 map overlay/fog/FOW/flatmap branches. If not, document that visual parity is limited to core water and cannot match EID 1061 exactly.
4. Audit water DDS color-space loading against CK3 resource formats.
5. Replace or calibrate Ocean lighting toward CK3 water lighting constants before relying on Stride-style `RiverStrideLighting`.

Docs to read before next session:
- `Terrain/Effects/Ocean/OceanSurface.sdsl`
- `Terrain/Effects/Water/WaterRefractionCapture.sdsl`
- `Terrain/Rendering/Ocean/OceanRenderFeature.cs`
- `Terrain/Rendering/CustomForwardRenderer.cs`

---

## Session Statistics

Files changed: 1 documentation log.
Code files changed: 0.
Commits: 0.

---

## Quick Reference

Artifacts exported locally:
- `.codex/renderdoc/save_1061`
- `.codex/renderdoc/debug_ocean_280`
- `.codex/renderdoc/debug_refraction_246`

Do not commit `.codex/**`.

