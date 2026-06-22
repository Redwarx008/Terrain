# Baked Detail Texture Export Boundary

**Date**: 2026-06-22
**Status**: ✅ Accepted
**Decision ID**: ADR-016

---

## Context

- Runtime startup previously generated full detail control textures from `.terrain` height data plus authoring `biome_mask.png` and `biome_settings.toml`.
- That made runtime startup slower and forced runtime bootstrap to require editor authoring resources.
- The `.terrain` payload already carries virtual texture data, so detail control should be exported once and streamed at runtime.

---

## Decision

- `.terrain` v8 contains exactly `HeightMap VT + DetailIndex VT + DetailWeight VT`.
- `.terrain` does not contain `BiomeMask VT`.
- `biome_mask.png` and `biome_settings.toml` remain Editor/Export authoring inputs.
- Editor Export bakes detail control into two RGBA8 packed virtual texture streams using `DetailControlPixel`.
- Runtime streams `DetailIndex` and `DetailWeight` pages from `.terrain` and no longer compiles or exposes runtime detail generation / biome mask reader APIs.

---

## Options Considered

### Option 1: Keep Runtime Generation
**Description:**
- Runtime continues to build detail control maps at startup.

**Pros:**
- No `.terrain` format migration.

**Cons:**
- Slow startup.
- Runtime depends on editor authoring files.
- Detail generation logic remains in the wrong layer.

### Option 2: Store BiomeMask VT In `.terrain`
**Description:**
- Export a BiomeMask virtual texture, then generate detail from it at runtime.

**Pros:**
- Avoids requiring `biome_mask.png` separately at runtime.

**Cons:**
- Still keeps detail generation in runtime.
- Adds an intermediate payload that runtime does not need after baking.

### Option 3: Store Baked DetailIndex/DetailWeight VT
**Description:**
- Export final detail control streams and let runtime stream them directly.

**Pros:**
- Fast startup.
- Runtime only consumes runtime-ready data.
- Authoring resources stay in Editor/Export.

**Cons:**
- Requires `.terrain` v8 reader/writer migration.
- Export must validate unsupported authoring cases such as texture mask modifiers.

---

## Rationale

- Runtime should consume packaged data, not derive it from editor authoring rules.
- The renderer already needs RGBA8 detail index/weight texture arrays, so `.terrain` should store that final control data.
- Reusing generic `StreamMipLevels<DetailControlPixel>` keeps the VT writer path shared with existing export mechanics without adding a special byte-level RGBA writer.

---

## Trade-offs

**What we gain:**
- Startup no longer spends time building full detail maps.
- Runtime bootstrap no longer requires `biome_mask.png` or `biome_settings.toml`.
- `.terrain` has a clearer runtime contract.

**What we give up:**
- Changing biome rules requires re-exporting `.terrain`.
- Texture mask modifiers need explicit bake support before they can be exported.

---

## Consequences

### Positive
- Runtime `TerrainProcessor` and `TerrainStreamingManager` are simpler.
- Detail control streaming uses the same page residency model as height streaming.
- Tests now cover writer/reader roundtrip, exact payload length, and runtime absence of old generated-detail APIs.

### Negative
- Export does more work and needs access to authoring biome resources.
- Existing v7 `.terrain` files are no longer supported by the runtime reader.

### Neutral
- Historical docs and old session logs may still mention Runtime DetailMap generation, but current architecture docs and tests define the active contract.

---

## Implementation Notes

- `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs` owns authoring biome rule evaluation and emits `DetailControlPixel[]` for index/weight maps.
- `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs` writes HeightMap, DetailIndex, and DetailWeight streams, using temp-file commit semantics.
- `Terrain/Streaming/TerrainStreaming.cs` rejects `.terrain` files with trailing bytes after the expected v8 payload.
- Runtime old files `RuntimeDetailMapBuilder.cs`, `TerrainDetailGeneration.cs`, and `RuntimeBiomeMaskReader.cs` were removed.

---

## Related Decisions

- [ADR-012 Biome Rule Layer System](./adr-012-biome-rule-layer-system.md)
- [ADR-015 Workspace Game Root and Runtime Requirements](./adr-015-workspace-game-root-and-runtime-requirements.md)

---

## References

- [Baked detail texture export design](../../superpowers/specs/2026-06-22-baked-detail-texture-export-design.md)
- [Implementation plan](../../superpowers/plans/2026-06-22-baked-detail-texture-export.md)

---

*ADR Template Version: 1.0*
