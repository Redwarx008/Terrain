# CK3 River Final Postprocess 1146
**Date**: 2026-06-19
**Session**: Follow-up
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Explain why CK3 river appears very dark during river/main-scene draws but becomes correct deep blue at draw/event `1146`.

**Secondary Objectives:**
- Verify whether `1146` is a river surface draw or a final postprocess draw.
- Hot-replace the current final pass before editing SDSL.

**Success Criteria:**
- Separate surface/main-scene linear color from final display-space color.
- Determine whether current black/brown output can be fixed by exposure alone.

---

## Context & Background

**Previous Work:**
- Related: `docs/log/2026/06/19/2026-06-19-river-surface-no-terrain-see-through-black.md`

**Current State:**
- Current `debug.rdc` river surface/main-scene output is dark and brown-biased at representative pixels.
- CK3 `ck3-river.rdc` also has dark river values before final presentation.

**Why Now:**
- User observed that CK3 is mostly black while drawing, then suddenly becomes visually correct at draw/event `1146`.

---

## What We Did

### 1. Verified CK3 Event 1146
**Files Changed:** None

**Investigation:**
- Opened `C:\Users\Redwa\Desktop\ck3-river.rdc`.
- Event `1146` is `numIndices=3`, outputs to swapchain `ResourceId::94` (`B8G8R8A8_UNORM`).
- PS bindings are `TonyMcMapfaceLUT_Texture`, `MainScene_Texture`, `RestoreBloom_Texture`, `ColorCube_Texture`, and `FogBlurTexture_Texture`.
- PS cbuffer includes `FixedExposureValue=2.0`, `Contrast=1.14`, `Pivot=0.18`, and `TonemapIndex=0.02745098`.

**Rationale:**
- Event `1146` is the final display composite / tonemap / LUT / color-grade pass, not a river mesh surface draw.

### 2. Compared CK3 Linear/Main-Scene And Final Display Pixels
**Files Changed:** None

**Investigation:**
- CK3 river/main-scene pixel at event `466`, `(110,738)`: approximately `[0.022, 0.028, 0.030, 1]`.
- Same pixel after event `1146`: approximately `[0.192, 0.235, 0.251, 0.227]`.

**Rationale:**
- CK3's river is expected to look almost black in the intermediate linear/HDR target. The final pass lifts it into visible display-space blue.

### 3. Hot-Replaced Current Final Swapchain PS
**Files Changed:** None

**Investigation:**
- Opened current `C:\Users\Redwa\Desktop\debug.rdc`.
- Final event `832` outputs to `R8G8B8A8_SRGB` swapchain and uses a Stride tonemap PS that samples only `Texture0`.
- Current representative pixel `(854,632)`:
  - Surface event `149`: `[0.070, 0.062, 0.046, 1]`.
  - Final event `832`: `[0.122, 0.106, 0.071, 0.012]`.
- Hot-replaced event `832` PS with simplified CK3-like exposure/contrast/display curve.
- Hot result at `(854,632)`: `[0.647, 0.631, 0.588, 1]`.
- Restored the original shader after verification.

**Rationale:**
- Exposure/contrast alone brightens the current frame, but keeps it beige/brown. It does not turn the water blue.

### 4. Isolated WaterColorTexture From Bottom/Refraction
**Files Changed:** None

**Investigation:**
- Hot-replaced CK3 surface event `466` to output only `WaterColorTexture.Sample(...)` using the shader's own world/map UV path.
- CK3 representative pixel `(110,738)`:
  - Direct `WaterColorTexture`: `[0.0117, 0.0504, 0.0564]`.
  - Same replacement after CK3 event `1146`: `[0.0706, 0.3333, 0.3529]`.
- Hot-replaced current surface event `149` to output only `WaterColorTexture_id118` using `PositionWS.xz * _WorldToMapUnitScale / _MapWorldSize` with Y flip.
- Current representative pixel `(854,632)`:
  - Direct `WaterColorTexture`: `[0.0032, 0.0136, 0.0150]`.
  - Same replacement through current original final event `832`: `[0.0000, 0.0000, 0.0039]`.
  - Same replacement through simplified CK3-like final lift: `[0.0000, 0.3412, 0.3765]`.
- Restored all hot-replaced shaders after verification.

**Rationale:**
- A dark blue/cyan `WaterColorTexture` sample is CK3-compatible. The low linear value is not itself a river surface bug.
- The current final tonemap does not lift this low water-color range like CK3's event `1146`.
- When using a CK3-like display lift, current isolated water color lands close to CK3's final blue/cyan range, despite being darker before final.

---

## Decisions Made

### Decision 1: Do Not Treat CK3 Event 1146 As River Surface Evidence
**Context:** Event `1146` visually fixes the color, but it is not the river shader.

**Decision:** Use `1146` only as evidence for final postprocess behavior.

**Rationale:** River shader equivalence must be judged at the matching river surface/main-scene target. Final visual equivalence also requires matching the postprocess/color-grade chain.

### Decision 2: Do Not Patch SDSL Yet
**Context:** Hot replacement showed current exposure can brighten but not correct hue.

**Decision:** Continue diagnosing input color source and final color-grade mismatch before changing `RiverSurface.sdsl`.

**Rationale:** Current issue is split:
- Current final pass is not CK3's final mapface/LUT/ColorCube pass.
- Current surface/main-scene color is already brown-biased, unlike CK3's dark blue-biased intermediate values.

---

## What Worked ✅

1. **Checking draw semantics instead of event number alone**
   - Event `1146` has `numIndices=3` and swapchain output, so it is clearly a fullscreen final pass.

2. **Hot-replacing final PS**
   - Simplified exposure verified that brightness alone is insufficient; current chroma remains wrong.

---

## What Didn't Work ❌

1. **Comparing current surface RT directly against CK3 final screen output**
   - These are different color spaces and different pipeline stages.
   - CK3 intermediate river output is also very dark.

---

## Problems Encountered & Solutions

### Problem 1: CK3 Looks Black Before Event 1146 But Correct After
**Symptom:** River is dark during draw sequence, then becomes deep blue at event `1146`.

**Root Cause:** Event `1146` is the final postprocess chain: main scene + bloom + fog blur + exposure + contrast + mapface LUT + color cube.

**Investigation:**
- Verified event `1146` draw info, bindings, cbuffer, and pixel values.
- Verified current final pass has only Stride tonemap and no CK3 LUT/ColorCube chain.
- Hot-replaced current final PS with exposure/contrast.

**Solution:** No code change yet.

**Pattern for Future:** Always compare river surface/main-scene buffers to CK3's corresponding intermediate buffer, then separately compare final display-space output after postprocess.

---

## Architecture Impact

### Documentation Updates Required
- [ ] Add this final-postprocess comparison rule to river rendering learnings.
- [ ] Decide whether the project should replicate CK3 final mapface/color-grade pass or only use it as a reference for target screenshots.

### New Anti-Pattern
**New Anti-Pattern:** Treat CK3 final swapchain color as direct evidence of river surface shader output.

---

## Code Quality Notes

### Testing
- No automated tests changed.
- Manual RenderDoc hot replacement and pixel checks were performed.

### Technical Debt
- Current rendering does not have a CK3-equivalent final mapface LUT / ColorCube chain.
- Current surface/main-scene river color remains brown-biased at representative pixels.

---

## Next Session

### Immediate Next Steps
1. Compare CK3 river surface/main-scene pixels against current surface/main-scene pixels at equivalent semantic locations.
2. Trace why current surface event `149` produces `[R > G > B]` brown-biased output while CK3 intermediate river pixels are dark but blue-biased.
3. Separately decide whether to implement a CK3-like final mapface postprocess in Stride, or keep using Stride tonemap and tune river inputs against that display pipeline.

### Docs to Read Before Next Session
- `docs/log/2026/06/19/2026-06-19-river-surface-no-terrain-see-through-black.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Session Statistics

**Files Changed:** 1 documentation file
**Lines Added/Removed:** Documentation only
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- CK3 event `1146` is final postprocess, not river surface.
- CK3 event `1146` uses `MainScene`, `RestoreBloom`, `FogBlur`, `TonyMcMapfaceLUT`, and `ColorCube`.
- CK3 representative pixel went from `[0.022,0.028,0.030]` before final to `[0.192,0.235,0.251]` after final.
- Current event `832` final pass is Stride tonemap over one texture.
- Current hot exposure made `(854,632)` bright beige `[0.647,0.631,0.588]`, not blue.
- Isolated `WaterColorTexture` is dark in both captures. CK3 `[0.0117,0.0504,0.0564]` becomes `[0.0706,0.3333,0.3529]` after event `1146`; current `[0.0032,0.0136,0.0150]` stays black through Stride final but becomes `[0.0000,0.3412,0.3765]` through a simplified CK3-like lift.

**Gotchas for Next Session:**
- Do not compare current event `149` directly to CK3 event `1146`.
- Do not assume final brightness correction fixes hue.
- Restore hot-replaced shaders before continuing analysis; this session restored all replacements.

---
