# Export Terrain Performance
**Date**: 2026-06-22
**Session**: Export Terrain baked detail hot-path optimization
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Reduce `Export Terrain` latency after moving DetailIndex/DetailWeight baking into Editor Export.

**Secondary Objectives:**
- Preserve existing `.terrain` v8 output behavior.
- Add regression coverage so the hot path does not regain per-texel allocations.

**Success Criteria:**
- Baked detail export behavior tests continue to pass.
- Per-texel `List` / `Dictionary` / LINQ top4 aggregation is removed from the detail bake and detail mip paths.
- Documentation records the performance-sensitive pattern.

---

## Context & Background

**Previous Work:**
- [Baked detail texture export](2026-06-22-baked-detail-texture-export.md)
- [Export Terrain progress window](2026-06-22-export-terrain-progress-window.md)
- [ADR-016 baked detail texture export](../../decisions/adr-016-baked-detail-texture-export.md)

**Current State:**
- Runtime startup no longer builds DetailTexture data.
- Editor Export now bakes `DetailIndex` and `DetailWeight` into `.terrain` v8.

**Why Now:**
- User reported `Export Terrain` is too slow after the baked detail export migration.

---

## What We Did

### 1. Optimized Baked Detail Texel Evaluation
**Files Changed:** `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`

- Removed per-texel `new List<MaterialContribution>()`.
- Replaced LINQ `OrderByDescending().ThenBy().Take(4).ToArray()` with fixed top-four insertion over a reusable contribution buffer.
- Added a direct `ushort[]` height-data context to avoid per-height-sample delegate invocation in the normal export path.
- Restored the original validation behavior for biome mask dimensions and kept exact top-four tie-break comparison.

### 2. Optimized Detail Mip Aggregation
**Files Changed:** `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`

- Removed per-destination-texel `Dictionary<byte,int>` allocation.
- Replaced LINQ sort/sum/top4 with fixed stack buffers for the 2x2 source texel contribution set.
- Preserved previous detail mip semantics: aggregate duplicate material weights, sort by weight descending, then material index ascending.

### 3. Added Behavior And Hot-Path Regression Tests
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs`

- Added a mixed-modifier test proving the direct `ushort[]` height path matches the functional height path across height, slope, curvature, direction, noise, invert, opacity, and blend mode usage.
- Added a source regression test preventing per-texel collection/LINQ sorting from returning.

---

## Decisions Made

### Decision 1: Preserve Output Semantics Over Broader Algorithm Changes
**Context:** The export path writes runtime data, so even small channel-order or tie-break changes can affect terrain appearance.

**Decision:** Only remove allocation/sorting mechanics and keep the existing sampling order, validation, modifier math, fallback material behavior, and top-four tie-break rules.

**Trade-offs:** This avoids riskier changes such as caching slope/direction maps or changing rule evaluation order. Further CPU reductions can be done later with a measured benchmark.

---

## What Worked ✅

1. **Existing Behavior Tests**
   - The current baked detail tests quickly caught a behavior regression where the new direct height-data path initially skipped biome mask dimension validation.

2. **Fixed Small Buffers**
   - The hot path only ever needs a bounded contribution set, so fixed `Span<T>`/array buffers are a better fit than general-purpose collections.

---

## Problems Encountered & Solutions

### Problem 1: Direct Height Path Bypassed Existing Validation
**Symptom:** `editor baked detail builder requires biome mask detail dimensions` failed.

**Root Cause:** The optimized `ushort[]` overload entered the shared private generator after constructing a context and skipped the old public overload dimension checks.

**Solution:** Added shared `ValidateDetailInputs` and called it from all public generation overloads before constructing contexts.

### Problem 2: Tie-Break Could Have Changed With Epsilon
**Symptom:** Review of the change showed a new epsilon in material contribution ordering.

**Root Cause:** The old LINQ ordering used exact float comparison and then `FirstOrder`.

**Solution:** Removed epsilon and restored exact `>` / `==` semantics.

### Problem 3: Stable Priority Ordering Was Not Preserved
**Symptom:** Subagent review found that replacing `OrderBy(...).ToArray()` with `Array.Sort(...)` could change equal-`PriorityOrder` layer ordering.

**Root Cause:** LINQ `OrderBy` is stable, while `Array.Sort` is not guaranteed to preserve source order for equal keys.

**Solution:** Sort an `OrderedBiomeRuleLayer` array by `PriorityOrder` and then original source index. Added a behavior test proving equal-priority layers retain previous stable ordering semantics.

### Problem 4: Function Overload Validation Order Regressed
**Symptom:** Subagent review found that the `Func<int,int,float>` overload could validate computed detail dimensions before `getHeight` and height dimensions.

**Root Cause:** The optimized overload computed half resolution before restoring the old public overload guard order.

**Solution:** Restored explicit `getHeight`, `heightWidth`, and `heightHeight` validation at the start of the overload. Added tests for exception type and parameter names.

---

## Architecture Impact

### Documentation Updates Completed
- [x] Updated `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Updated `docs/CURRENT_FEATURES.md`
- [x] Added `docs/log/learnings/fixed-buffer-top4-hot-paths.md`

### New Pattern
**Fixed-buffer top4 aggregation for hot paths**
- Use when a hot loop repeatedly aggregates a bounded number of contributions.
- Avoid per-item heap allocations and LINQ sorting.
- Always preserve and test tie-break semantics.

---

## Code Quality Notes

### Performance
- Removed per-detail-texel `List` allocation and LINQ top4 sort in `BakedDetailMapBuilder`.
- Removed per-detail-mip-texel `Dictionary` allocation and LINQ top4 sort in `TerrainExporter`.
- The normal export path now samples `ushort[]` height data directly instead of calling through a delegate.

### Testing
- `dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passes.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj` passes all export-related tests, including the new behavior/hot-path tests and subagent-review follow-up tests for stable equal-priority ordering, function overload validation, and equal-weight detail mip ordering.
- The full run still fails two pre-existing scene asset text tests:
  - `runtime scene contains river system component`
  - `runtime compositor registers river render feature`

### Technical Debt
- No benchmark harness exists for real project-size Export Terrain data. A future performance pass should add a repeatable export benchmark using representative heightmap/biome settings sizes.

---

## Next Session

### Immediate Next Steps
1. Re-run Export Terrain manually on the real workspace and compare wall-clock time.
2. If still slow, profile `WriteMipPages` and `HeightmapLoader.GenerateMinMaxErrorMaps`; both remain full-map export stages.
3. Resolve or intentionally update the existing `Terrain/Assets/MainScene.sdscene` and `Terrain/Assets/GraphicsCompositor.sdgfxcomp` text-test failures.

### Docs to Read Before Next Session
- [fixed-buffer-top4-hot-paths](../../../learnings/fixed-buffer-top4-hot-paths.md)
- [ADR-016 baked detail texture export](../../../decisions/adr-016-baked-detail-texture-export.md)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Export performance issue was addressed without changing `.terrain` output semantics.
- Baked detail top4 tie-break remains: builder uses contribution first-order; detail mip aggregation uses material index.
- Current full test failures are from unrelated scene/compositor asset state.

**Gotchas for Next Session:**
- Do not reintroduce per-texel `List`, `Dictionary`, or LINQ sort in bake/mip paths.
- If optimizing further, use measured export timings and keep file-format roundtrip tests passing.
