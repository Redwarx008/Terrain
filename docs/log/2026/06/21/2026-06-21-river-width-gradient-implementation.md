# River Width Gradient Implementation
**Date**: 2026-06-21
**Session**: river width gradient implementation
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add configurable river width range to `game/map/default.toml`.
- Preserve local `rivers.png` palette width gradients during mesh generation.

---

## What We Did

### 1. Map settings
- Added `[settings] river_min_width` and `river_max_width` as full-width values.
- Defaults are `1` and `4`.
- Reader, writer, scaffold, and save path preserve the settings.

### 2. Local river widths
- Palette indices now map linearly into the configured full-width range.
- `RiverSegment` carries width samples aligned with cells and centerlines.
- `RiverMeshService` uses per-centerline half-widths instead of one segment average.
- Width data is stored as `CellHalfWidths` and `CenterlineHalfWidths` during generation.

---

## Testing

- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`
- `dotnet build Terrain.sln --no-restore`
- `git diff --check`

---

## Quick Reference for Future Claude

- TOML values are full-width.
- Default range is `river_min_width = 1` and `river_max_width = 4`.
- `rivers.png` palette indices map linearly across that full-width range.
- Shader width stream remains normalized half-width.
- Do not reintroduce `AvgHalfWidth` as the mesh's only width source.
