# Baked Detail Texture Export
**Date**: 2026-06-22
**Session**: DetailTexture export migration
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Move DetailTexture generation out of Runtime startup and into Editor Export.

**Secondary Objectives:**
- Make `.terrain` the runtime source for HeightMap + DetailIndex + DetailWeight.
- Remove runtime dependency on `biome_mask.png` / `biome_settings.toml`.
- Delete runtime detail generation / biome mask reader APIs.

**Success Criteria:**
- Runtime startup does not build detail maps.
- `.terrain` contains no BiomeMask VT.
- Exported `.terrain` can be opened by runtime reader and stream height/index/weight pages.
- Tests and builds pass.

---

## Context & Background

**Previous Work:**
- Related design: [baked-detail-texture-export-design](../../../../superpowers/specs/2026-06-22-baked-detail-texture-export-design.md)
- Related plan: [baked-detail-texture-export plan](../../../../superpowers/plans/2026-06-22-baked-detail-texture-export.md)
- Related ADR: [ADR-016 baked detail texture export](../../decisions/adr-016-baked-detail-texture-export.md)

**Current State Before This Session:**
- Runtime generated `RuntimeDetailMapData` during `ApplyLoadedTerrainData`.
- Runtime bootstrap required `biome_mask.png` and `biome_settings.toml`.
- `.terrain` still used old splat/biome mask terminology and did not carry final baked detail control streams.

**Why Now:**
- Startup was too slow because Runtime built full DetailTexture data on launch.
- Detail generation is authoring/export logic and should not live in the runtime assembly.

---

## What We Did

### 1. Locked Runtime Contract With Tests
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/*`

- Added failing tests for `.terrain` v8, DetailIndex/DetailWeight reader API, no generated runtime detail state, and runtime not requiring biome authoring resources.
- Removed old tests that expected runtime biome mask failure.

### 2. Migrated `.terrain` Reader To v8
**Files Changed:** `Terrain.Editor/Models/TerrainFileFormat.cs`, `Terrain/Streaming/TerrainStreaming.cs`

- `.terrain` v8 now exposes HeightMap, DetailIndex, and DetailWeight streams.
- Reader validates RGBA8 detail streams, half-resolution dimensions, matching headers, exact payload length, and constructor failure cleanup.
- Reader rejects trailing bytes after the expected v8 payload.

### 3. Switched Runtime Streaming To Baked Detail Pages
**Files Changed:** `Terrain/Streaming/TerrainStreaming.cs`, `Terrain/Core/TerrainProcessor.cs`

- `TerrainStreamingManager` reads `ReadDetailIndexPage` and `ReadDetailWeightPage` from `.terrain`.
- Runtime no longer creates or passes generated detail maps during startup.
- Tests now observe fake reader calls for index/weight pages.

### 4. Removed Runtime Authoring Resource Dependency
**Files Changed:** `Terrain/Resources/GameRuntimeResourceBootstrap.cs`, `Terrain/Resources/TerrainRuntimeResourceBundle.cs`

- Runtime bootstrap no longer resolves `map/biome_mask.png` or `map/biome_settings.toml`.
- `.terrain` and material descriptor remain runtime requirements.
- Local launch tests cover missing authoring biome resources.

### 5. Added Editor Baked Detail Builder
**Files Changed:** `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`

- Editor builder converts authoring biome rules into two `DetailControlPixel[]` buffers: DetailIndex and DetailWeight.
- Material contributions are aggregated by material slot before top4 selection and normalization.
- TextureMask modifiers fail fast until explicit bake support exists.
- Material slot index must be `0..254`; `255` remains shader sentinel.

### 6. Exported Baked Detail VT Payloads
**Files Changed:** `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`

- Export now writes `.terrain` v8 as HeightMap VT + DetailIndex VT + DetailWeight VT.
- Detail streams use `StreamMipLevels<DetailControlPixel>` with RGBA8 payloads.
- Detail mip generation aggregates 2x2 material contributions instead of copying one texel.
- Export writes to a same-directory temp file and commits via replace/move, preserving existing targets on failure/cancel.

### 7. Deleted Runtime Generation APIs
**Files Changed:** `Terrain/Materials/`, `Terrain/Resources/`, `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`

- Deleted `RuntimeDetailMapBuilder.cs`, `TerrainDetailGeneration.cs`, and `RuntimeBiomeMaskReader.cs`.
- Deleted obsolete runtime biome mask reader tests.
- Added negative migration tests so runtime-facing code does not expose old APIs again.

---

## Decisions Made

### Decision 1: `.terrain` Stores Final Detail Control, Not BiomeMask
**Context:** Runtime only needs texture control pages, not authoring biome masks.

**Options Considered:**
1. Keep runtime generation - no format migration, but startup stays slow.
2. Store BiomeMask VT - removes separate PNG dependency but still keeps runtime generation.
3. Store DetailIndex/DetailWeight VT - runtime streams final data directly.

**Decision:** Chose option 3.
**Rationale:** Runtime should consume packaged runtime-ready data.
**Trade-offs:** Biome rule changes require re-exporting `.terrain`.
**Documentation Impact:** Added ADR-016 and updated architecture/current feature docs.

### Decision 2: Reuse `StreamMipLevels<DetailControlPixel>`
**Context:** Detail control streams are RGBA8 virtual textures.

**Decision:** Use a 4-byte packed pixel type, not `StreamMipLevels<byte>` or a new RGBA writer.
**Rationale:** Keeps export VT writing generic and makes element size match reader bpp validation.

---

## What Worked ✅

1. **Subagent-Driven Review Gates**
   - Worker commits were reviewed for spec and quality after each task.
   - This caught fake v8 export, weak builder tests, unsafe TextureMask handling, and detail mip downsample semantics before final integration.

2. **Red Tests First For Runtime Boundary**
   - Tests drove the removal of runtime generation, bootstrap authoring dependencies, and old API exposure.

---

## What Didn't Work ❌

1. **Fake v8 Export During Reader Migration**
   - Early Task 2 code wrote v8 headers while still emitting old R8 BiomeMask payload.
   - Fix: exporter failed fast until real DetailIndex/DetailWeight export was implemented.

2. **Top4 While Streaming Contributions**
   - Early builder logic discarded duplicate material contributions before all layers were known.
   - Fix: aggregate by material slot first, then select top4 and normalize.

---

## Problems Encountered & Solutions

### Problem 1: Runtime Would Still Require Authoring Files
**Symptom:** Test failed on missing `map/biome_mask.png`.
**Root Cause:** Runtime bootstrap still resolved authoring resources even though processor no longer used them.
**Solution:** Remove biome mask/settings fields from `TerrainRuntimeResourceBundle` and bootstrap.

### Problem 2: Detail Mips Lost Material Contributions
**Symptom:** Downsample initially copied the 2x2 upper-left texel.
**Root Cause:** Index/weight pairs need semantic aggregation, not color sampling.
**Solution:** Decode 2x2 source index/weight pixels, aggregate material weights, select top4, and repack index/weight together.

---

## Architecture Impact

### Documentation Updates Completed
- [x] Added [ADR-016](../../decisions/adr-016-baked-detail-texture-export.md)
- [x] Updated [ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md)
- [x] Updated [CURRENT_FEATURES.md](../../../../CURRENT_FEATURES.md)
- [x] Updated [README.md](../../../../../README.md)

### Architectural Decisions That Changed
- **Changed:** Runtime detail map source.
- **From:** Runtime generates `RuntimeDetailMapData` from authoring biome resources at startup.
- **To:** Editor Export bakes RGBA8 DetailIndex/DetailWeight VT streams into `.terrain`.
- **Scope:** Runtime bootstrap, `.terrain` reader/writer, streaming manager, editor exporter, tests.
- **Reason:** Reduce startup cost and move authoring logic to Editor layer.

---

## Code Quality Notes

### Testing
- Tests cover `.terrain` v8 reader validation, exporter roundtrip, exact payload length, trailing byte rejection, temp-file preservation on cancel, detail mip aggregation, runtime streaming reader calls, and old runtime API removal.

### Technical Debt
- TextureMask modifiers are not baked yet; export fails clearly when they are encountered.
- Detail mip byte weights use rounded normalized channels and may sum to 256 in some cases; current shader path accepts approximate normalized weights, but residual distribution can be improved if exact byte sums become necessary.

---

## Next Session

### Immediate Next Steps
1. Add TextureMask modifier baking support if map authoring starts using texture mask rules.
2. Run an editor/manual export smoke test against a real `game/` dataset and inspect resulting runtime scene.
3. Consider residual distribution for mip weight packing if visual artifacts appear in distant detail LODs.

### Docs to Read Before Next Session
- [ADR-016](../../decisions/adr-016-baked-detail-texture-export.md)
- [Baked detail plan](../../../../superpowers/plans/2026-06-22-baked-detail-texture-export.md)

---

## Session Statistics

**Files Changed:** Multiple runtime, editor, test, and documentation files
**Commits:** 19 implementation/design commits before this documentation update

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `.terrain` v8 runtime payload is `HeightMap VT + DetailIndex VT + DetailWeight VT`.
- Runtime no longer knows how to parse `biome_mask.png` or build detail maps from biome rules.
- Editor export owns detail baking via `BakedDetailMapBuilder`.

**What Changed Since Last Doc Read:**
- Runtime `GameRuntimeResourceBootstrap` no longer requires biome authoring resources.
- `RuntimeDetailMapBuilder`, `TerrainDetailGeneration`, and `RuntimeBiomeMaskReader` were deleted.
- `TerrainExporter` writes baked detail VT payloads and commits through a temp file.

**Gotchas for Next Session:**
- Do not reintroduce BiomeMask VT into `.terrain`.
- Do not use `StreamMipLevels<byte>` for RGBA8 detail; use `DetailControlPixel`.
- TextureMask modifiers are intentionally unsupported for baked export until a real mask sampler is implemented.

---

## Links & References

### Related Documentation
- [ADR-016](../../decisions/adr-016-baked-detail-texture-export.md)
- [Architecture Overview](../../../../ARCHITECTURE_OVERVIEW.md)
- [Current Features](../../../../CURRENT_FEATURES.md)

### Code References
- `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`
- `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`
- `Terrain/Streaming/TerrainStreaming.cs`
- `Terrain/Core/TerrainProcessor.cs`

---

## Notes & Observations

- The subagent review loop was useful here because binary file format and startup behavior had several easy-to-miss cross-layer traps.
- Historical session logs and old plans still mention runtime detail generation; use ADR-016 and current tests as the active contract.
