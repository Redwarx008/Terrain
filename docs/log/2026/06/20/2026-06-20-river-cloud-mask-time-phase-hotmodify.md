# River Cloud Mask Time Phase Hotmodify
**Date**: 2026-06-20
**Session**: River RenderDoc cloud-mask follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Compare `C:\Users\Redwa\Desktop\debug3.rdc` and `C:\Users\Redwa\Desktop\debug4.rdc` after repeated terrain visibility toggles produced different river colors.
- Use RenderDoc MCP shader hot replacement only, without local code hot edits.

**Success Criteria:**
- Identify whether the river color difference comes from resources/bindings, water normal lighting, refraction, or wrapper post-processing.
- Sample multiple pixels to verify the cause.

---

## Context & Background

Previous work fixed editor tone-map auto exposure. New captures still showed different river colors after toggling terrain visibility multiple times. Both captures use the same river surface draw at EID 149.

---

## What We Did

### 1. Reconfirmed the River Draw Difference

**Captures Compared:**
- `debug3.rdc`
- `debug4.rdc`

**Key Observations:**
- EID 149 river surface input values are identical at matching pixels.
- Resource bindings and shader IDs are the same.
- The only exported Globals buffer difference found earlier is `_GlobalTime`: `265.66` in `debug3`, `293.66` in `debug4`.
- Original output at `(836,498)`:
  - `debug3`: `[0.0295728, 0.0580255, 0.0634051, 1]`
  - `debug4`: `[0.152915, 0.254559, 0.240982, 1]`

### 2. Hot-Replaced Specular Probe

**Finding:**
- `specularLight * _WaterSpecularFactor` was effectively zero at the representative pixel.
- The 4x color difference is not a high-gloss specular amplification.

### 3. Hot-Replaced CalcRefraction Probe

**Finding:**
- Full `CalcRefraction` output at `(836,498)` was bright in both captures:
  - `debug4`: `[0.146368, 0.248379, 0.234603, 1]`
  - `debug3`: `[0.146368, 0.248379, 0.234603, 1]`
- Therefore the dark `debug3` output is not caused by flow normal, direct lighting, specular, or refraction body.

### 4. Hot-Replaced Wrapper Mask Probe

**Probe Output:** `(shadowTintMask, cloudMask, terrainShadowTerm, 1)`

**debug3:**
- `(836,498)`: `[0, 1, 1, 1]`
- `(800,490)`: `[0, 1, 1, 1]`
- `(870,506)`: `[0, 1, 1, 1]`
- `(910,516)`: `[0, 1, 1, 1]`

**debug4:**
- `(836,498)`: `[0, 0, 0, 1]` in the cloud-only probe
- `(800,490)`: `[0, 0, 0, 1]`
- `(870,506)`: `[0, 0, 0, 1]`
- `(910,516)`: `[0, 0, 0, 1]`

The dark output is explained exactly by:

```hlsl
color.rgb = lerp(color.rgb, float3(0.0f, 0.01f, 0.02f), cloudMask * 0.8f);
```

With `cloudMask=1`, `[0.146, 0.248, 0.235]` becomes approximately `[0.029, 0.058, 0.063]`, matching `debug3`.

---

## Decisions Made

### Decision 1: Do Not Treat Terrain Visibility as the Direct Cause

**Context:** The captures were produced by toggling terrain visibility, but river inputs/resources remained identical.

**Decision:** Treat `_GlobalTime`-driven procedural cloud phase as the immediate cause of the color difference.

**Rationale:** Toggling visibility advanced time between captures. At one time phase the river region is covered by procedural cloud shadow; at the other phase it is not.

---

## What Worked ✅

1. **Hot replacement at shader boundaries**
   - Specular, refraction, and wrapper masks were isolated independently.

2. **Multiple pixel confirmation**
   - The cloud mask result held across several river pixels, not just one representative point.

---

## What Didn't Work ❌

1. **Attributing the color difference to flow normal or specular first**
   - Flow normal changed with `_GlobalTime`, but direct lighting and specular probes did not explain the magnitude.

2. **Stopping at CalcRefraction**
   - `CalcRefraction` was identical and bright in both captures; the decisive change happened in wrapper post-processing.

---

## Architecture Impact

### Documentation Updates Required
- [x] Add a learning note about procedural cloud mask time phase.
- [ ] No architecture overview update required; no implementation changed.
- [ ] No current feature update required; no feature status changed.

### New Anti-Pattern Discovered
**Anti-Pattern:** Treating terrain visibility toggles as causality before checking `_GlobalTime`-driven wrapper effects.

**Why it's bad:** Time-varying cloud shadow can produce a large river color shift while resources, bindings, inputs, and refraction remain identical.

---

## Next Session

### Immediate Next Steps
1. Decide desired editor behavior:
   - Keep CK3-style animated cloud shadow in the editor, accepting time-varying river color.
   - Disable river surface cloud tint in editor debug mode.
   - Freeze cloud time for river rendering while doing visual parity captures.
2. If implementing a fix, prefer an explicit editor/debug setting rather than removing wrapper cloud tint globally.

---

## Session Statistics

**Files Changed:** 1 log file plus 1 learning note
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `debug3` dark river is caused by `GetCloudShadowMask(...)=1`, not by terrain visibility directly.
- `debug4` bright river has `GetCloudShadowMask(...)=0`.
- The time delta between captures changes `_GlobalTime`, moving procedural cloud shadow across the river.

**Gotchas for Next Session:**
- When comparing river captures, either freeze `_GlobalTime` or output cloud mask before blaming resources, normals, refraction, or tone mapping.
