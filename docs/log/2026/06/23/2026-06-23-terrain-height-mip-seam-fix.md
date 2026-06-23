# Terrain Height Mip Seam Fix
**Date**: 2026-06-23
**Session**: terrain-height-mip-seam-fix
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Diagnose the returned visible seams in `C:\Users\Redwa\Desktop\debug.rdc`.

**Success Criteria:**
- Identify whether the seam is raster state, shader color, river rendering, or actual terrain geometry.
- Apply the smallest fix that addresses the root cause and add a regression test.

---

## Context & Background

**Previous Work:**
- See: `docs/log/2026/06/23/2026-06-23-river-height-source-bilinear-fix.md`

**Current State:**
- The capture shows dotted/short black gaps on the terrain mesh, near but not caused by the river pass.

**Why Now:**
- The user corrected the initial direction: the likely root cause was vertex height mismatch, not culling.

---

## What We Did

### 1. RenderDoc Diagnosis
**Files Changed:** none

**Findings:**
- Terrain main color draw is event 158, `DrawIndexedInstanced`, using `MaterialTerrainDisplacement`.
- Pixel history on seam pixels shows terrain event 158 does not write them; they stay clear until the later fullscreen/background draw. This proves true geometry holes, not a color or overlay issue.
- Post-VS mesh traces found adjacent LOD terrain vertices at the same world XZ `(8192, 3296)` with different heights:
  - `lod3` chunk `(31,12)`: `POSITION_WS.y = 22.592506`
  - `lod4` chunk `(16,6)`: `POSITION_WS.y = 22.330053`
- The same world coordinate was sampled from different HeightMap VT mips, so averaged height mips could produce mismatched shared-edge vertices.

### 2. Height Mip Export Fix
**Files Changed:** `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`

**Implementation:**
- Replaced the old averaged `DownsampleHeights` path with `GenerateAlignedHeightMip`, which uses even-aligned source sample inheritance.
- Made the height mip generator `internal` so the invariant can be tested directly.

**Rationale:**
- HeightMap mips are geometry sources, not color textures. Crack snap aligns edge vertex positions across LODs, but it also requires shared boundary heights to match across mip levels. Averaging makes the coarse LOD edge a filtered height while the fine LOD edge remains a different sample.

### 3. Regression Test
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs`

**Implementation:**
- Added `terrain exporter height mips preserve aligned source samples`.
- The test verifies mip1 and mip2 preserve source values at aligned coordinates and far edges.

---

## Decisions Made

### Decision 1: Fix Height Mip Generation, Not Shader Sampling
**Context:** The shader already uses the intended LOD-specific VT page and RenderDoc proved the mismatch exists in the height values read at shared world coordinates.

**Decision:** Preserve exact aligned source samples in exported HeightMap mip levels.

**Rationale:** This fixes the data invariant used by runtime LOD crack snapping without adding neighbor-page bindings or shader-side special cases.

**Trade-offs:** Coarser HeightMap mip levels no longer average height over 2x2 areas. That is acceptable because these mips drive geometry continuity; visual smoothing should not come from changing edge vertex heights.

---

## What Worked ✅

1. **RenderDoc pixel history + post-VS mesh trace**
   - It separated true geometry gaps from color/shading artifacts and gave exact mismatched world-space heights.

2. **Data-invariant regression test**
   - Testing `GenerateAlignedHeightMip` directly locks the cause without needing GPU automation.

---

## What Didn't Work ❌

1. **Culling/raster-state hypothesis**
   - What we tried: briefly changed terrain rasterizer culling.
   - Why it failed: pixel history and vertex traces showed actual gaps from mismatched vertex heights.
   - Lesson learned: when seam pixels have no terrain writer in pixel history, verify shared-edge vertex positions and heights before changing render state.

---

## Architecture Impact

### Documentation Updates Required
- [x] Updated `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Updated `docs/CURRENT_FEATURES.md`

### New Pattern
**VT mips are semantic data**
- HeightMap VT mips must preserve aligned source samples across levels.
- Detail/control mips must not average RGBA values. They decode 2x2 source index/weight pixels, aggregate weights by material id, select top4, normalize, and repack.

---

## Code Quality Notes

### Testing
- **Tests Written:** 1 focused regression test.
- **Verification:** `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`
- **Status:** Passed. Existing NuGet vulnerability warnings remain.

### Runtime Note
- Existing exported `.terrain` files still contain old averaged height mips. Re-export terrain data before taking a fresh RenderDoc capture to verify the visual seam is gone.

---

## Next Session

### Immediate Next Steps
1. Re-export the active `.terrain` from the editor.
2. Capture a new RenderDoc frame and compare the same seam area.
3. If any seam remains, trace the exact shared-edge pair again and verify both source mip values now match.

---

## Session Statistics

**Files Changed:** 5 content files
**Tests:** Full `Terrain.Editor.Tests` executable passed

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Root cause was height mip averaging, not river rendering and not culling.
- Fix is in `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`.
- The active runtime asset must be re-exported; source changes alone do not rewrite existing `.terrain` payloads.

**Gotchas for Next Session:**
- Do not debug stale captures or stale `.terrain` exports as if the new mip rule is present.
- `DetailIndex` / `DetailWeight` mip aggregation is intentionally separate from HeightMap mip generation. It should not be changed to RGBA averaging or point sampling because of this fix.
