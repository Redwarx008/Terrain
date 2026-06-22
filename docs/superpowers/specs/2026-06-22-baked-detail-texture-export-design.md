# Baked DetailTexture Export Design

**Date:** 2026-06-22
**Status:** Ready for review

---

## Goal

Runtime currently builds the terrain DetailTexture during startup by evaluating biome rules over the whole map. That makes startup slow and forces Runtime terrain loading to depend on authoring inputs such as `biome_mask.png` and `biome_settings.toml`.

Move DetailTexture construction to Editor Export. A `.terrain` file should contain the baked runtime detail control textures that terrain rendering needs directly.

---

## Decisions

1. `.terrain` v8 is the runtime terrain artifact.
2. `.terrain` v8 does **not** contain `BiomeMask VT`.
3. `.terrain` v8 contains `DetailIndexMap VT` and `DetailWeightMap VT`.
4. Runtime terrain detail loading reads baked detail pages from `.terrain`; it does not build detail maps at startup.
5. Detail generation logic belongs to the Editor/Export layer. Runtime must not retain `RuntimeDetailMapBuilder` or terrain-detail rule evaluation code.
6. `biome_mask.png`, `biome_settings.toml`, and related rule state remain authoring inputs used by Editor and Export.

---

## File Format

`.terrain` v8 payload order:

1. `TerrainFileHeader`
2. `MinMaxErrorMap[]`
3. `HeightMap VTHeader + tiles`
4. `DetailIndexMap VTHeader + tiles`
5. `DetailWeightMap VTHeader + tiles`

`HeightMap` stays unchanged:

- Format: R16
- `BytesPerPixel = 2`
- Padding: existing heightmap padding, currently `2`

Both detail maps use runtime shader-ready control data:

- Format: RGBA8 / `R8G8B8A8_UNorm`
- `BytesPerPixel = 4`
- Dimensions: `(heightWidth + 1) / 2` by `(heightHeight + 1) / 2`
- Resolution ratio to heightmap: `2`
- Padding: existing detail/splat padding, currently `1`
- Mip generation: nearest sample, matching the current runtime generated-detail page sampling semantics. Do not average indices or weights across texels.

Header naming should be moved away from the current `SplatMap*`/`BiomeMask` ambiguity. Runtime/editor file-format structs should expose detail-specific names such as:

- `DetailMapFormat`
- `DetailMapMipLevels`
- `DetailMapResolutionRatio`

The VT payload headers remain authoritative for width, height, padding, bytes per pixel, and mip count. DetailIndex and DetailWeight headers must match each other.

Runtime should reject old v6/v7 `.terrain` files with a clear message requiring re-export. There is no runtime fallback to build detail maps from authoring resources.

---

## Editor Export

`TerrainExporter` becomes responsible for baking detail control data before writing `.terrain`.

Inputs:

- Current height data snapshot from `TerrainManager`
- Current `BiomeMask`
- Current terrain dimensions and `HeightScale`
- Current biome/layer/modifier state from `BiomeRuleService`
- Current material slot to runtime material index mapping from `MaterialSlotManager`

Output:

- `DetailIndexMapData`: RGBA8 byte data, four material indices per texel
- `DetailWeightMapData`: RGBA8 byte data, four material weights per texel

The detail generator should live under `Terrain.Editor`, for example in `Terrain.Editor/Services/Export` or a nearby authoring/detail namespace. Runtime should not reference it. If a future command-line preprocessor needs the same logic, extract an authoring/preprocess assembly later rather than keeping it in the Runtime project.

The exporter needs a proper RGBA8 VT write path. The existing VT writer streams unmanaged pixels by tile/mip, but detail data should be represented as 4-byte pixels, not as independent bytes. A small packed detail-control pixel type or equivalent structured writer should preserve RGBA byte order explicitly.

Export progress should add a visible detail-bake step before writing the `.terrain` file. Export failure should continue to use the existing rollback behavior.

---

## Runtime Loading

Runtime terrain initialization no longer loads or validates `biome_mask.png` for terrain detail generation.

`TerrainFileReader` should expose baked detail VT page reads:

- `DetailIndexMapHeader`
- `DetailWeightMapHeader`
- `DetailMapResolutionRatio`
- `DetailMapMipCount`
- `ReadDetailIndexPage(TerrainPageKey key, Span<byte> destination)`
- `ReadDetailWeightPage(TerrainPageKey key, Span<byte> destination)`

`TerrainStreamingManager` should:

- Allocate the existing GPU `DetailIndexMapArray` and `DetailWeightMapArray`
- Compute detail page keys using `DetailMapResolutionRatio`
- Read detail index and weight pages directly from `.terrain` on the IO thread
- Upload the two RGBA8 pages to the existing GPU texture arrays

`TerrainStreamingManager` should delete:

- `RuntimeDetailMapData? generatedDetailMaps`
- `SetGeneratedDetailMaps(...)`
- `FillGeneratedDetailPage(...)`
- IO-thread detail generation from full CPU arrays

`TerrainProcessor` should delete:

- `RuntimeDetailMapBuilder.Generate(...)` startup path
- `DetailMapBuilder` delegate plumbing
- Runtime biome-mask loading used only for detail generation

`TerrainComponent.GetHeight` remains for runtime height queries such as river mesh generation. It is no longer part of the terrain DetailTexture startup path.

Runtime still needs `materials/descriptor.toml` and material textures because terrain shading needs material arrays. Runtime does not need `biome_settings.toml` for terrain detail control after this change.

---

## Tests

Remove or reverse old tests that assert runtime detail generation:

- Runtime detail map builds after terrain streaming is attached
- Runtime detail map uses `TerrainComponent.GetHeight`
- Runtime streaming contains `generatedDetailMaps`
- Runtime streaming calls `FillGeneratedDetailPage`

Add tests for the new boundary:

- Export writes `.terrain` version `8`
- Export writes HeightMap, DetailIndex, and DetailWeight VT payloads
- Export does not write BiomeMask VT
- Detail VT payloads use `BytesPerPixel = 4`
- DetailIndex and DetailWeight dimensions/mip counts match
- Runtime rejects v6/v7 `.terrain` files and asks for re-export
- Runtime reader can read DetailIndex/DetailWeight pages by `TerrainPageKey`
- Runtime startup does not load `biome_mask.png` or call `RuntimeDetailMapBuilder`
- Streaming IO reads detail pages from `.terrain`
- A small heightmap + biome mask + rules export produces detail bytes that match the Editor detail evaluator

Verification commands:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain\Terrain.csproj --no-restore
dotnet build Terrain.Editor\Terrain.Editor.csproj --no-restore
git diff --check
```

---

## Migration Impact

Existing v6/v7 `.terrain` files must be re-exported. This is intentional: old files do not contain the baked detail control data Runtime now requires.

Documentation that currently says `.terrain` persists a BiomeMask and Runtime rebuilds detail maps from it must be updated:

- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/design/map-data-toml-formats.md`
- relevant session logs or ADRs if implementation makes a durable architecture decision

---

## Non-Goals

- Do not add Runtime fallback detail generation.
- Do not keep BiomeMask VT in `.terrain`.
- Do not move biome authoring state out of Editor.
- Do not change material texture loading in this work.
- Do not change river height sampling or river resource loading.
