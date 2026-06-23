# Runtime Terrain VT Page Replacement Fix
**Date**: 2026-06-23
**Session**: Runtime terrain flicker follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Investigate runtime terrain flicker / sudden low-detail fallback while roaming with `new.terrain`, focusing on VT page-table replacement rather than changing quadtree LOD selection.

**Success Criteria:**
- Keep the existing LOD algorithm unchanged.
- Fix the page replacement issue that lets useful resident pages be evicted too early.
- Remove temporary diagnostics and earlier wrong-direction fixes after the root cause is confirmed.

---

## Context & Background

**Previous Work:**
- See: `docs/log/2026/06/23/2026-06-23-runtime-terrain-reference-review.md`
- User clarified that `LOD delta > 1` is expected, there is only one relevant runtime camera, there are no black lines, and the likely issue is page-table eviction/update/replacement logic.
- User also clarified that mip and LOD are different here: physical VT pages are fixed at 256, while terrain chunks may be 16/32/64/128.

**Current State:**
- Runtime repro confirmed the artifact disappeared after touching resident child pages during quadtree child-residency checks.
- Temporary diagnostics and unrelated rendering-path experiments have been removed.

---

## What We Did

### 1. Root Cause: Candidate Child Pages Were Cold in LRU
**Files Changed:** `Terrain/Rendering/TerrainQuadTree.cs`, `Terrain/Streaming/TerrainStreaming.cs`

**Implementation:**
- `TerrainQuadTree` now calls `IsChunkResident(childKeys[i], touchResidentPages: true)` while deciding whether all children can replace their parent.
- `TerrainStreamingManager.IsChunkResident(...)` keeps its default non-mutating behavior, but when `touchResidentPages` is true it calls `TryGetResidentSlice(...)` for the height and detail pages, refreshing their replacement recency.

**Rationale:**
- A child page can already be resident but not yet selected as a render node because its siblings are still missing. Those resident candidate pages were not being touched by normal render use, so the LRU could evict them as cold before the full child set became selectable. That produced repeated fallback to coarser terrain and visible low-detail/flicker during roaming.

### 2. Kept Narrow Streaming Fixes
**Files Changed:** `Terrain/Streaming/TerrainStreaming.cs`, `Terrain/Core/TerrainProcessor.cs`

**Implementation:**
- Top-level height/detail fallback pages are uploaded as pinned during preload.
- `RequestPage` no longer returns early when the height page is already queued; it still gets a chance to queue the matching detail page.

**Rationale:**
- Only the top fallback layer should be permanently resident. This is a design invariant and fallback hardening, not the confirmed flicker root cause.
- Duplicate height queuing should not starve the detail side of another chunk sharing the same height page. This matters because height pages and detail pages can have different page spans.

### 3. Kept Stale Draw-State Guard
**Files Changed:** `Terrain/Rendering/TerrainRenderFeature.cs`

**Implementation:**
- Empty runtime terrain selections set `renderObject.InstanceCount = 0` before returning.

**Rationale:**
- This is independent of the flicker root cause but prevents a RenderView with no selected nodes from reusing stale terrain draw state.

### 4. Rolled Back Earlier Wrong Directions
**Files Changed:** multiple temporary source/test/docs files

**Removed:**
- `TerrainSelectionDiagnostics` / `TerrainStreamingDiagnostics` and all `TERRAIN_*_DIAGNOSTICS` hooks.
- CPU neighbor-mask diagnostic bypass.
- `LodLookupBuffer` invalid-sentinel / clear-before-build changes.
- DX11-irrelevant explicit resident texture state transition workaround.
- Top-level preload API reshaping that passed `topMapLod` separately.

**Rationale:**
- The confirmed cause was page replacement recency, not LOD topology, neighbor masks, multiple cameras, shader invalid sentinels, or DX11 resource barriers.

---

## What Worked ✅

1. **User-guided page-table hypothesis**
   - The issue continued after LOD-focused checks and had no black-line symptom.
   - Focusing on eviction/update behavior led to the confirmed fix.

2. **LRU touch at the child-residency check**
   - It preserves the LOD algorithm and only changes replacement recency for pages that the quadtree is actively considering.

---

## What Didn't Work ❌

1. **LOD lookup / neighbor-mask direction**
   - `LOD delta > 1` is expected in this renderer.
   - The artifact did not match a crack/neighbor-mask failure and those changes were removed.

2. **DX11 barrier/resource-state direction**
   - The runtime target is Direct3D11, so the D3D12-style barrier explanation was not the right model.
   - The explicit transition workaround was removed.

3. **Heavy runtime diagnostics**
   - Diagnostics were useful during investigation but are not part of the final fix.
   - Keeping them would add noise and extra maintenance surface.

---

## Architecture Impact

### Documentation Updated
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

### Key Rule
- Only top-level fallback pages are permanently pinned.
- Non-top resident candidate pages stay evictable, but quadtree child-residency checks must refresh their LRU recency so they are not discarded while waiting for sibling pages.

---

## Testing

**Regression Coverage:**
- Runtime checks now cover:
  - detail page request is still queued when height page is already queued.
  - top-level fallback pages are pinned during preload.
  - quadtree child-residency checks touch resident pages.
  - empty runtime terrain selection clears `InstanceCount`.
- The detail queue regression is behavior-level: two chunks share the same queued height page but map to distinct detail pages, and both detail pages must be read from the fake terrain reader.

**Manual Validation:**
- User reproduced the runtime scenario and confirmed the issue was resolved after the resident-page touch fix.

---

## Gotchas for Next Session

- Do not treat `LOD delta > 1` as a bug.
- Do not reintroduce CPU neighbor-mask bypass or LOD diagnostic machinery for this symptom.
- Do not use D3D12 barrier reasoning for this Direct3D11 runtime issue.
- Keep mip and LOD separate: VT page size is fixed at 256, while chunk block sizes can vary.
