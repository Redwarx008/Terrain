# Export Terrain Parallel Detail Bake
**Date**: 2026-06-22
**Session**: Parallelize baked detail map generation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Further reduce `Export Terrain` time by parallelizing large detail map bake work.

**Secondary Objectives:**
- Keep baked `.terrain` output deterministic and behaviorally equivalent.
- Avoid adding parallel overhead for small test maps or small authoring maps.

**Success Criteria:**
- Large detail texel bake uses multiple cores.
- Large detail mip downsample uses multiple cores.
- Existing behavior tests and export roundtrip tests still pass.

---

## Context & Background

**Previous Work:**
- [Export Terrain performance](2026-06-22-export-terrain-performance.md)
- [Fixed buffer top4 hot paths](../../../learnings/fixed-buffer-top4-hot-paths.md)

**Current State Before This Session:**
- Detail bake no longer allocated per-texel `List` / `Dictionary` / LINQ top4 structures.
- `Create DetailMap` was still mostly single-threaded over every detail texel.

**Why Now:**
- User reported `create detail map` is still too slow and asked whether it can be parallelized further.

---

## What We Did

### 1. Parallelized Detail Texel Evaluation
**Files Changed:** `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`

- Added `ParallelDetailTexelThreshold`.
- For large maps, `Generate` now uses `Parallel.For` over detail rows.
- Each worker gets its own `MaterialContribution[]` buffer, so contribution aggregation remains thread-local.
- Each row writes to a disjoint output slice in `DetailIndex` and `DetailWeight`, preserving deterministic output.
- Small maps stay on the previous serial loop to avoid scheduling overhead.
- Function-backed height callbacks stay serial; only array-backed export data enters the parallel path.
- If a worker throws, the builder replays serial evaluation to surface the same first exception the old row-major path would have thrown instead of leaking `AggregateException`.

### 2. Parallelized Detail Mip Downsample
**Files Changed:** `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`

- Added `ParallelDetailMipTexelThreshold`.
- Large detail mip levels now downsample rows with `Parallel.For`.
- `PackDownsampledDetailTexel` still uses stack-local fixed buffers, so there is no shared mutable aggregation state.
- Small mip levels remain serial.

### 3. Added Regression Guard
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs`

- Added `editor baked detail builder keeps parallel row evaluation`.
- Added large-map validation tests for direct invalid-slot / TextureMask exceptions, high-priority layer shielding of lower invalid layers, and serial function-backed height callbacks.
- The 513 detail texel tests exercise the builder threshold path; the 1025-height / 513-detail export test exercises the detail mip downsample threshold path and still validates `.terrain` reader roundtrip and mip aggregation behavior.

---

## Decisions Made

### Decision 1: Row-Level Parallelism With Thresholds
**Context:** Detail texels and mip texels are independent, but parallel scheduling overhead is not free.

**Decision:** Parallelize by row only when total texel count is at least `65_536` and the machine has more than one processor.

**Rationale:** Row-level partitioning keeps writes disjoint and avoids per-texel tasks. The threshold keeps small maps/tests cheap.

**Trade-offs:** The threshold is heuristic. A future benchmark on real map sizes can tune it.

---

## What Worked ✅

1. **Disjoint Output Slices**
   - Rows map directly to unique output ranges, so the parallel path does not need locks.

2. **Thread-Local Contribution Buffers**
   - Reuses the fixed-buffer pattern safely under `Parallel.For`.

---

## Problems Encountered & Solutions

### Problem 1: Verification Commands Were Initially Run In Parallel
**Symptom:** `dotnet build` and `dotnet run` failed with `Terrain.dll` locked by another process / `VBCSCompiler`.

**Root Cause:** Both commands were launched simultaneously, and Stride assembly processing writes the same output files.

**Solution:** Ran `dotnet build-server shutdown`, then reran build and tests serially.

---

## Architecture Impact

### Documentation Updates Completed
- [x] Updated `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Updated `docs/CURRENT_FEATURES.md`
- [x] Updated `docs/log/learnings/fixed-buffer-top4-hot-paths.md`

### New Pattern
**Thresholded row-level parallel bake**
- Use for large, independent texel/pixel computations.
- Keep per-worker buffers local.
- Write only disjoint output ranges.
- Keep small inputs serial.

---

## Code Quality Notes

### Performance
- Detail texel evaluation now uses multiple cores for large maps.
- Detail mip downsample now uses multiple cores for large mip levels.
- No locks were introduced on the hot path.
- Validation errors keep their original exception types on the large-map path.

### Testing
- `dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passes.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj` passes all Export/baked detail related tests, including the new parallel row evaluation guard.
- The full run still fails two pre-existing scene asset text tests:
  - `runtime scene contains river system component`
  - `runtime compositor registers river render feature`

### Technical Debt
- No wall-clock benchmark exists yet for representative real map export sizes. The current threshold is conservative and should be tuned with a benchmark if export remains slow.

---

## Next Session

### Immediate Next Steps
1. Run a manual `Export Terrain` on the real project and compare wall-clock time before/after this change.
2. If still slow, profile `WriteMipPages` and `HeightmapLoader.GenerateMinMaxErrorMaps`.
3. Consider a small benchmark harness for representative map sizes.

### Docs to Read Before Next Session
- [Export Terrain performance](2026-06-22-export-terrain-performance.md)
- [Fixed buffer top4 hot paths](../../../learnings/fixed-buffer-top4-hot-paths.md)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Large detail bake and mip downsample are now row-parallel.
- Output determinism relies on disjoint row writes and thread-local contribution buffers.
- Function-backed height callbacks intentionally remain serial; only the production array-backed export path is parallelized.
- Do not share `MaterialContribution[]` across parallel workers.

**Gotchas for Next Session:**
- Do not run build and test commands for this solution concurrently; Stride assembly processor can lock shared output assemblies.
- If changing thresholds, validate with real export timings, not only synthetic small tests.
