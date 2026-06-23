# River Width Palette Diagnosis
**Date**: 2026-06-21
**Session**: river width palette diagnosis
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Confirm whether river generation follows the `rivers.png` color-width convention.

**Success Criteria:**
- Identify where the palette is defined.
- Identify how generated mesh width consumes that palette.
- State whether local wide/thin gradients are preserved.

---

## Context & Background

**Previous Work:**
- Related: [2026-06-19-river-current-vs-ck3-bank-input-width-mismatch.md](./2026-06-19-river-current-vs-ck3-bank-input-width-mismatch.md)
- Related: [2026-06-19-river-surface-refraction-depth-hotmodify-analysis.md](./2026-06-19-river-surface-refraction-depth-hotmodify-analysis.md)

**Current State:**
- `RiverPixelType.WidthPalette` maps river colors from light blue through darker blue into green to increasing half-widths.
- `RiverMapService.ComputeAvgWidth` averages palette widths across each extracted segment.
- `RiverMeshService.BuildRiverMesh` uses `segment.AvgHalfWidth * widthScale` as one base width for the whole segment, only modified by endpoint taper.

---

## What We Did

### 1. Checked width palette and mesh generation
**Files Changed:** none

**Findings:**
- The palette definition exists and matches the expected gradient:
  - light blue starts at half-width `0.500`
  - blue values increase through half-width `1.500`
  - green values continue to half-width `1.625` and `1.750`
- The generation path does not preserve per-pixel width changes along the river.
- Instead, it computes a segment average width and applies that width along the whole mesh segment.

---

## Decisions Made

### Decision 1: Local width gradient is not fully honored
**Context:** The user clarified that the light-blue to dark-blue, then dark-green to light-green gradient defines river width constraints.
**Decision:** Treat current generation as only partially honoring that convention.
**Rationale:** The palette is parsed correctly, but local width variation is collapsed into `AvgHalfWidth`.
**Trade-offs:** This simplifies mesh generation but loses local thick/thin constraints from `rivers.png`.

---

## Next Session

### Immediate Next Steps
1. If fixing this, carry width samples alongside centerline samples instead of only storing `AvgHalfWidth`.
2. Interpolate/smooth width with the centerline and use per-centerline-point half-width in `BuildRiverMesh`.
3. Add a regression test with a segment containing both narrow light-blue and wide green pixels, asserting generated vertex widths vary along the mesh.

---

## Session Statistics

**Files Changed:** 1 documentation log
**Lines Added/Removed:** documentation only
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `Terrain.Editor/Models/RiverPixelType.cs` has the correct color-to-half-width palette.
- `Terrain.Editor/Services/RiverMapService.cs` collapses the palette to `AvgHalfWidth`.
- `Terrain.Editor/Services/RiverMeshService.cs` uses that average as `baseHalfWidth`, so local palette gradients are not preserved.

---

