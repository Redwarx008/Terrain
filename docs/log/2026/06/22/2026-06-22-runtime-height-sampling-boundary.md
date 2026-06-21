# Runtime Height Sampling Boundary
**Date**: 2026-06-22
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Remove the full `ushort[]` heightmap residency introduced for runtime rivers and keep `TerrainComponent` aligned with `.terrain` streaming.

---

## What Changed

- `TerrainComponent` no longer stores `RuntimeHeightData` or exposes a river-specific height source.
- `TerrainComponent.GetHeight(int sampleX, int sampleZ)` is the public runtime height query.
- `TerrainHeightSampler` reads the requested `.terrain` height tile on demand and keeps a fixed 4-tile CPU page cache.
- Runtime DetailMap generation now also calls `TerrainComponent.GetHeight` instead of reading a full `ushort[]` heightmap.
- `RiverProcessor` passes `TerrainComponent.GetHeight` to `RiverMeshService`.
- `RiverMeshService` owns river-specific bilinear interpolation by requesting 4 discrete terrain samples.

---

## Decision

`TerrainComponent` exposes discrete height samples only. It is responsible for locating/loading the backing tile; callers decide whether they need nearest, bilinear, or another reconstruction strategy.

This preserves the streaming model: Runtime DetailMap and River both use the same component interface, and neither path asks `TerrainProcessor` to materialize the full heightmap.

---

## Verification

- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`
- `dotnet build Terrain.sln --no-restore`
- `git diff --check`

Result: passed with existing warnings only.
