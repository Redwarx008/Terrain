# River Surface No-Terrain See-Through Black
**Date**: 2026-06-19
**Session**: Follow-up
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Diagnose the updated `C:\Users\Redwa\Desktop\debug.rdc`, where terrain was not drawn and river surface blackness appeared view-dependent.

**Secondary Objectives:**
- Confirm the previous depth-based surface alpha fix reached the GPU.
- Use RenderDoc hot replacement before any SDSL edits.
- Separate raw refraction input from surface see-through/post-processing.

**Success Criteria:**
- Identify the pass and shader path causing the view-dependent darkening.
- Record which suspected paths were ruled out.

---

## Context & Background

**Previous Work:**
- Previous session fixed surface alpha to CK3 depth-based alpha.
- Related: `docs/log/2026/06/19/2026-06-19-river-surface-depth-alpha-hotmodify.md`
- Related learning: `docs/log/learnings/stride-river-rendering-patterns.md`

**Current State:**
- Updated capture timestamp: 2026-06-19 20:02:42.
- Capture has no terrain draw in the visible frame, but river bottom/surface passes still execute.

**Why Now:**
- User observed that the river surface remains black and the blackness changes with camera angle.

---

## What We Did

### 1. Verified The Active Surface Shader
**Files Changed:** None

**Investigation:**
- Opened `C:\Users\Redwa\Desktop\debug.rdc` via RenderDoc MCP.
- Draw chain remained:
  - `94/108`: river bottom pass.
  - `149/176`: river surface pass.
- Surface PS `ResourceId::7831` includes:
  - `waterColor.a = saturate(depth * 2.0f / max(_Depth_id31, 0.0001f)) * transparency * connectionFade;`
- Search for old `edgeFade1 * edgeFade2` only matched bottom PS `ResourceId::7826`.

**Rationale:**
- This ruled out stale shader binaries and the previous bottom-style surface alpha bug.

### 2. Hot-Replaced Surface With Raw Refraction
**Files Changed:** None

**Investigation:**
- Replaced surface PS with a minimal shader that outputs `RefractionTexture.rgb` using screen UV plus CK3-style alpha.
- Result: river became visibly much brighter.

**Rationale:**
- The bottom/refraction input was not black.
- The darkening happens inside the surface shader path after raw refraction is available.

### 3. Probed See-Through Attenuation And Water Color Map
**Files Changed:** None

**Investigation:**
- Replaced surface PS with diagnostic output:
  - `R = attenuation` from `CalcTerrainUnderwaterSeeThrough`.
  - `G = refractionDepth / 5`.
  - `B = toCameraDir.y`.
- Representative dark pixels:
  - `(854,632)`: attenuation about `0.26`, refraction depth about `0.95`, `toCameraDir.y` about `0.56`.
  - `(717,715)`: attenuation about `0.21`, refraction depth about `1.20`, `toCameraDir.y` about `0.62`.
  - `(1058,315)`: attenuation about `0.23`, refraction depth about `0.60`, `toCameraDir.y` about `0.33`.
- Replaced surface PS to output `WaterColorTexture` sampled at refraction-world UV:
  - `(854,632)` about `[0.008, 0.010, 0.014]`.
  - `(717,715)` about `[0.008, 0.011, 0.015]`.
  - `(1058,315)` about `[0.002, 0.005, 0.007]`.
- Replaced surface PS to output `WaterColorTexture` sampled at surface-world UV; it was similarly dark.

**Rationale:**
- With attenuation around `0.2-0.26`, the shader keeps only about 20-26% of raw refraction and replaces the rest with a near-black water color map.
- This exactly matches the view-dependent blackening: shallower `toCameraDir.y` increases water distance and reduces attenuation.

---

## Decisions Made

### Decision 1: Do Not Patch SDSL Yet
**Context:** Hot edits showed two possible fixes: skip see-through in no-terrain mode, or provide a brighter/valid `WaterColorTexture`.

**Options Considered:**
1. Disable `CalcTerrainUnderwaterSeeThrough` for rivers - fixes this capture but diverges from CK3.
2. Set `_WaterSeeThroughDensity = 0` for no-terrain debug captures - targeted but needs an explicit mode/setting.
3. Keep CK3 shader semantics and fix/replace the current `River/Water/water-color` input or provide a no-terrain fallback.

**Decision:** No SDSL change this session.

**Rationale:** The active shader is CK3-equivalent in the relevant see-through formula; the current blackness is caused by the input water color map being near-black in a no-terrain capture.

**Trade-offs:** The visual issue remains until we choose whether the desired behavior is CK3 fidelity or no-terrain debug resilience.

---

## What Worked ✅

1. **Raw refraction replacement**
   - What: Output `RefractionTexture.rgb` directly from surface PS.
   - Why it worked: Immediately separated input RT validity from surface shading.

2. **Packed diagnostic replacement**
   - What: Output attenuation/refraction-depth/to-camera-y as RGB.
   - Impact: Proved the view-dependence comes from see-through attenuation.

3. **WaterColorTexture replacement**
   - What: Output the exact water color map sampled by surface-world and refraction-world UV paths.
   - Impact: Proved the replacement color is near-black.

---

## What Didn't Work ❌

1. **Continuing to focus on alpha**
   - What we tried: Verified the active alpha formula and pixel history.
   - Why it failed as root cause: Surface alpha fix was already compiled into the GPU shader.
   - Lesson learned: Once alpha is validated, split refraction and see-through before touching more alpha logic.

---

## Problems Encountered & Solutions

### Problem 1: Surface Is Black Only From Some Views
**Symptom:** In the no-terrain capture, river surface darkens depending on camera angle.

**Root Cause:** `CalcTerrainUnderwaterSeeThrough` computes low attenuation at these angles and depths, then lerps most of the color toward `WaterColorTexture`, whose sampled RGB is near black.

**Investigation:**
- Tried: raw refraction output.
- Tried: attenuation/refraction-depth/to-camera-y diagnostic.
- Tried: refraction-world and surface-world `WaterColorTexture` output.
- Found: refraction input is bright, but water color map is near black and attenuation is low.

**Solution:** Not applied yet.

**Why This Works:** The fix depends on the intended behavior:
- CK3 fidelity: bind/provide a valid water color map matching CK3 capture assumptions.
- No-terrain debug resilience: disable or reduce see-through map influence when terrain/water-color context is unavailable.

**Pattern for Future:** For view-dependent water darkening, probe attenuation and water-color map before changing alpha or fog.

---

## Architecture Impact

### Documentation Updates Required
- [x] Update learnings with the no-terrain see-through diagnostic pattern.
- [ ] Decide whether to add a no-terrain river surface fallback setting.

### New Anti-Pattern
**New Anti-Pattern:** Treat no-terrain black surface as an alpha/FoW/height problem after raw refraction is proven bright.

---

## Code Quality Notes

### Testing
- No automated tests changed because no code was changed.
- Manual GPU verification was performed via RenderDoc hot replacement.

### Technical Debt
- The renderer does not currently expose an explicit no-terrain surface see-through fallback.

---

## Next Session

### Immediate Next Steps
1. Decide desired behavior for no-terrain captures: CK3 fidelity with valid `water-color` input, or debug fallback that skips see-through map influence.
2. If choosing fallback, add a setting/parameter around `_WaterSeeThroughDensity` or map influence and verify with RenderDoc.
3. If choosing CK3 fidelity, compare current `River/Water/water-color.dds` against CK3 resource and confirm binding/content.

### Questions to Resolve
1. Should no-terrain debug mode keep CK3 see-through physics, or should it prioritize visible raw refraction?
2. Is the current static `water-color.dds` the intended CK3 resource for this scene scale?

### Docs to Read Before Next Session
- `docs/log/2026/06/19/2026-06-19-river-surface-depth-alpha-hotmodify.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Session Statistics

**Files Changed:** 2 documentation files
**Lines Added/Removed:** Documentation only
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Active surface draw IDs: `149/176`.
- Active surface PS: `ResourceId::7831`.
- Alpha fix is present in GPU.
- Raw refraction hot replacement makes the surface bright.
- See-through attenuation at representative dark pixels is about `0.21-0.26`.
- `WaterColorTexture` sampled color at those pixels is about `0.002-0.015`.

**What Changed Since Last Doc Read:**
- Architecture: no code change.
- Implementation: no code change.
- Constraints: no-terrain debug captures need explicit treatment if we do not want CK3 see-through to consume the dark static water-color map.

**Gotchas for Next Session:**
- Do not reopen the alpha investigation unless a new capture proves the shader regressed.
- Do not blame `FogOfWarAlpha`; it is not bound in this project and this capture's root is upstream of FOW.
- Remember that height slices/post wrapper can affect final RGB, but this capture's primary black source is see-through plus dark `WaterColorTexture`.

---

## Links & References

### Code References
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`

### External References
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\river_surface.shader`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_water_default.fxh`

---

## Notes & Observations

- `WaterCubemapIntensity` is `0` in the current capture, so reflection cannot rescue dark see-through.
- CK3 uses the same broad see-through structure, but it assumes a valid water color map and environment context.
