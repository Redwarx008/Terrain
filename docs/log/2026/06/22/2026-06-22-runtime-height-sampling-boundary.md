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
- Runtime height sampling now stays inside `TerrainStreamingManager`; there is no standalone `TerrainHeightSampler` abstraction.
- CPU height page cache follows GPU residency: uploaded height pages stay available on CPU and are released when the corresponding GPU resident page is evicted by the `MaxResidentChunks` policy.
- Runtime DetailMap generation now runs after `ApplyLoadedTerrainData` attaches streaming/quad tree and calls `TerrainComponent.GetHeight` instead of reading a full `ushort[]` heightmap. The generated detail maps still feed `TerrainStreamingManager` as the existing detail VT page upload source.
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

Follow-up after removing standalone `TerrainHeightSampler`:

- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore` passed with existing warnings.
- `dotnet build Terrain/Terrain.csproj --no-restore` passed with existing warnings.
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore` passed with existing warnings.
- `git diff --check` passed.
- `dotnet build Terrain.sln --no-restore` was attempted but blocked by an external file lock: `Terrain.Windows (33208)` / Visual Studio held `Bin/Windows/Debug/win-x64/Terrain.dll`.

Follow-up after runtime hang at `lock (heightCacheGate)`:

- Root cause: the CPU height page cache used a single monitor shared by runtime height sampling, GPU upload caching, and GPU eviction callbacks. During large Runtime DetailMap generation this serialized unrelated work and could appear as the program stuck on the cache lock.
- Fix: replaced the monitor-protected `Dictionary` + linked-list LRU with `ConcurrentDictionary` + queue-based trimming. Uploaded height pages are marked GPU-resident and are not trimmed by the non-resident cache cap; they are released by `GpuVirtualTextureArray.PageEvicted` when the corresponding GPU page is evicted.
- Verification: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`, `dotnet build Terrain/Terrain.csproj --no-restore`, `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`, and `git diff --check` passed with existing warnings.
