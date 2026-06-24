# Ocean Conservative Energy Convergence
**Date**: 2026-06-24
**Session**: Ocean CK3 1061 conservative fix
**Status**: Complete pending fresh RenderDoc visual check
**Priority**: High

---

## Session Goal

Implement the agreed conservative convergence plan for Ocean after comparing current `debug.rdc` against CK3 `save.rdc` EID 1061.

---

## Context & Background

Previous RenderDoc comparison showed the current Ocean shader structure had moved closer to CK3 core water, but GPU constants and input energy were still far off:
- current shared refraction RGB max was about `[5.06, 3.25, 1.92]`;
- CK3 EID 1061 refraction RGB max was about `[0.39, 0.43, 0.39]`;
- current Ocean output reached about `[5.61, 5.64, 5.29]`;
- CK3 output stayed below about `[0.59, 0.50, 0.52]`.

Related session:
- `docs/log/2026/06/24/2026-06-24-ocean-current-vs-ck3-1061-parameter-compare.md`

---

## What We Did

### 1. Ocean-only shader energy scaling

Implemented the conservative plan without changing shared refraction capture, River, or global scene lighting:
- `_WaterFlowNormalScale = 0.025f`
- `_WaterDiffuseMultiplier = 0.0f`
- `_WaterSpecular = 0.01f`
- `_WaterGlossBase = 1.15f`
- `_WaterGlossScale = 0.1f`
- `_WaterReflectionIntensity = 0.025f`
- `_OceanSceneLightingScale = 0.05f`
- `_OceanRefractionColorScale = 0.085f`

`CalcRefraction` now scales only Ocean's consumed refraction RGB with `_OceanRefractionColorScale`. The shared capture texture remains RGB passthrough for River and any other consumers.

`RiverStrideComputeLighting` is still used for Ocean, but the Ocean pass now passes `_OceanSceneLightingScale` as the environment intensity scale.

### 2. CK3 captured water color defaults

Updated Ocean material defaults and the runtime scene's explicit Ocean material colors:
- Shallow: `[0.0055146287, 0.0078107193, 0.0120865023]`
- Deep: `[0.00013850747, 0.00019749513, 0.00022629512]`

### 3. Regression tests

Updated text tests to lock:
- the captured flow-normal and water energy defaults;
- Ocean-only refraction and lighting scale parameters;
- no introduction of province/border/FOW/flatmap or fixed CK3 strategy-light tokens;
- `MainScene.sdscene` and `OceanMaterialSettings` using the captured shallow/deep water colors.

---

## Decisions Made

No new architecture decision was made. This follows the explicit scope: conservative Ocean-only convergence while continuing to use scene lighting.

---

## Next Session

Immediate next step:
1. Capture a fresh `debug.rdc` and confirm Ocean EID constants reach GPU.
2. Check Ocean output max RGB is now below `1.0`.
3. If still too bright, adjust only `_OceanRefractionColorScale` using `0.085 * (0.6 / measuredOutputMax)`.

---

## Session Statistics

Files changed: Ocean shader/material defaults, runtime scene asset, tests, generated shader keys, docs.
Commits: 0.
