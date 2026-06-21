# Runtime Height Apply Review Fix
**Date**: 2026-06-22
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

Address subagent review findings for the runtime terrain height/detail-map loading boundary.

---

## What Changed

- `TerrainProcessor.Initialize` now marks runtime load success only after `ApplyLoadedTerrainData` completes.
- Apply-time failures are caught by the same runtime failure gate used for metadata/resource load failures.
- Apply-time cleanup disables the render object and disposes partially attached `TerrainQuadTree` / material state.
- `ApplyLoadedTerrainData` now tracks ownership of the newly created `TerrainStreamingManager`; if an exception occurs before it is attached to `TerrainQuadTree`, it is disposed locally.
- `TerrainComponent.GetHeight` now throws `InvalidOperationException` if terrain streaming is not initialized instead of returning `0.0f`.
- Added regression tests for uninitialized height sampling and apply-time failure gate handling.

---

## Decision

Runtime height sampling is only valid after terrain streaming is attached. Returning a fake zero height hides load-order bugs, so the component now fails fast.

The runtime load gate must cover the full initialization path, not only `.terrain` metadata and biome-mask loading. Runtime DetailMap generation depends on `TerrainComponent.GetHeight`, so it belongs to the failure boundary.

---

## Verification

- Added red tests, confirmed they failed before the fix:
  - `terrain component get height fails before streaming attaches`
  - `runtime apply failure is captured by runtime load gate`
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore` passed with existing package/analyzer warnings.
- `dotnet build Terrain/Terrain.csproj --no-restore` passed with existing package warnings.
- `git diff --check` passed with existing CRLF conversion warnings only.

---

## Quick Reference

- Runtime apply failure handling: `Terrain/Core/TerrainProcessor.cs`
- Height sampling guard: `Terrain/Core/TerrainComponent.cs`
- Regression tests: `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs`
