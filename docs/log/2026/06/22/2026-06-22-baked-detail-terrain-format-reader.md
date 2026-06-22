# Baked Detail Terrain Format Reader
**Date**: 2026-06-22
**Session**: Task 2
**Status**: Partial
**Priority**: High

---

## Session Goal

Update the `.terrain` binary header and runtime reader boundary for baked detail texture export.

**Primary Objective:**
- Move the file format boundary to `.terrain` v8 with HeightMap VT + DetailIndex VT + DetailWeight VT.

**Success Criteria:**
- Header version is 8.
- Runtime reader rejects older terrain versions.
- Runtime reader exposes `DetailIndex` and `DetailWeight` VT headers and page reads.
- Reader validates both baked detail streams exist and are long enough.
- Current code compiles; expected red tests from later tasks are documented.

---

## What We Did

### 1. `.terrain` Header v8
**Files Changed:** `Terrain.Editor/Models/TerrainFileFormat.cs`, `Terrain/Streaming/TerrainStreaming.cs`

- Replaced old binary header fields for splat/river payloads with:
  - `DetailMapFormat`
  - `DetailMapMipLevels`
  - `DetailMapResolutionRatio`
- Bumped editor `TerrainFileHeader.CURRENT_VERSION` to `8`.
- Runtime reader now supports only version `8` and asks users to re-export old terrain files.

### 2. Runtime Reader Boundary
**Files Changed:** `Terrain/Streaming/TerrainStreaming.cs`

- Replaced reader API with baked detail names:
  - `DetailIndexMapHeader`
  - `DetailWeightMapHeader`
  - `DetailMapResolutionRatio`
  - `DetailMapMipCount`
  - `ReadDetailIndexPage`
  - `ReadDetailWeightPage`
- Removed old `ReadSplatMapPage` and legacy optional river-mask payload parsing.
- Added validation that detail payloads are RGBA8, half-resolution, padding 1, matching mip counts, matching headers, and present in the file.

### 3. Compile Boundary Adapters
**Files Changed:** `Terrain/Core/TerrainProcessor.cs`, `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`, `Terrain.Editor.Tests/VirtualResources/FakeTerrainFileReader.cs`

- Updated direct references from old reader/header names to the new detail names so the solution still compiles.
- Did not implement exporter baking, runtime streaming cleanup, or bootstrap dependency removal; those remain later tasks.

### 4. Tests
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/BakedDetailTerrainFormatTests.cs`

- Added a small binary reader fixture test proving missing/truncated `DetailWeight` stream is rejected.
- Existing Task 1 source-level format tests now pass for Task 2.

---

## Verification

Ran:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Result:
- Build succeeded.
- Task 2 format/reader tests passed.
- Expected remaining failures:
  - `bootstrap does not require biome authoring resources`
  - `runtime terrain startup does not build detail maps`
  - `runtime source no longer contains generated detail map state`

These failures belong to later tasks: runtime bootstrap cleanup and runtime generated-detail removal.

Also ran:

```powershell
dotnet build Terrain\Terrain.csproj --no-restore
```

Result:
- Build passed with existing NuGet advisory warnings and existing nullable warning.

---

## Next Session

1. Implement runtime streaming conversion so detail pages are uploaded from baked `.terrain` streams.
2. Stop runtime bootstrap from requiring biome authoring resources.
3. Remove runtime detail generation state and builder code.
4. Update exporter to bake and write real DetailIndex/DetailWeight RGBA8 VT payloads.

---

## Notes

- `Terrain\Runtime\TerrainFileFormat.cs` does not exist in the current repo. The active runtime format reader lives in `Terrain/Streaming/TerrainStreaming.cs`.
- `TerrainExporter` was touched only to keep the renamed header fields compiling; it still writes the old biome-mask payload and is expected to be fixed by the exporter task.
