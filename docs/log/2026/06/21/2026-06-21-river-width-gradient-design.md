# River Width Gradient Design
**Date**: 2026-06-21
**Session**: river width gradient design
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Design configurable river width range in `game/map/default.toml`.
- Design a fix for local `rivers.png` palette width gradients being collapsed into `AvgHalfWidth`.

**Success Criteria:**
- Confirm TOML field names and units.
- Document the data flow from palette index to generated mesh width.
- Leave implementation for the next TDD phase.

---

## Context & Background

**Previous Work:**
- See: [2026-06-21-river-width-palette-diagnosis.md](./2026-06-21-river-width-palette-diagnosis.md)

**Current State:**
- `RiverPixelType` parses palette indices from `rivers.png`.
- Current generation computes one `AvgHalfWidth` per segment.
- `RiverMeshService.BuildRiverMesh` uses that average width along the whole segment, with only endpoint taper changing it.

---

## What We Did

### 1. Confirmed config shape
**Files Changed:** `docs/superpowers/specs/2026-06-21-river-width-gradient-design.md`

**Decision:**
- Add `[settings] river_min_width = 1` and `river_max_width = 4`.
- Values are full-width. Mesh internals continue using half-width.

### 2. Designed local width propagation

**Decision:**
- Keep palette index as semantic source.
- Map palette index linearly into the configured full-width range.
- Carry width samples alongside centerline positions through simplification, smoothing, interpolation, and mesh generation.

---

## Decisions Made

### Decision 1: TOML values are full-width
**Context:** Existing comments describe palette entries as full-width values.
**Decision:** `river_min_width` and `river_max_width` use full-width.
**Rationale:** This matches user-facing map semantics and keeps shader full-width restoration unchanged.

### Decision 2: Fix generation by local width samples
**Context:** Segment averaging loses local shallow-blue/deep-blue/green width variation.
**Decision:** Store and resample local half-widths instead of using only `AvgHalfWidth`.
**Rationale:** This directly addresses the observed bug without changing mesh topology.

---

## Next Session

### Immediate Next Steps
1. Use TDD to add map definition reader/writer tests for river width settings.
2. Add mesh-generation regression coverage proving mixed palette widths produce varying vertex widths.
3. Implement the config model and local width sample propagation.

---

## Session Statistics

**Files Changed:** 2 documentation files
**Lines Added/Removed:** documentation only
**Commits:** 0 at log creation time

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- New config fields: `[settings] river_min_width`, `[settings] river_max_width`.
- They are full-width values, defaults `1` and `4`.
- Mesh generation should preserve local palette width gradient by carrying width samples, not by using only `AvgHalfWidth`.

---

