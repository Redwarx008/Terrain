# River Runtime Core Migration
**Date**: 2026-06-22
**Session**: river runtime core migration
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Execute Task 2+3 only: move river domain, mesh generation, rendering core, and SDSL shaders from `Terrain.Editor` into the runtime `Terrain` project.

**Success Criteria:**
- River domain types live under `Terrain.Rivers`.
- River render types live under `Terrain.Rendering.River`.
- River shaders live under `Terrain/Effects/River`.
- `Terrain` owns River shader key generation and the reflection-specular RootAsset.
- Editor keeps only the façade/service wiring.
- Tests advance past old namespace and generated shader key failures.

---

## Context & Background

**Previous Work:**
- See: [2026-06-22 terrain assembly resource root](2026-06-22-terrain-assembly-resource-root.md)
- Related: [ADR-014 river rendering architecture](../../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- The river implementation was functionally complete but editor-owned.
- Runtime scene/compositor wiring is intentionally left for Task 4+.

---

## What We Did

### 1. Moved River Domain and Mesh Generation
**Files Changed:** `Terrain/Rivers/*`, editor services/tests

**Implementation:**
- Moved `RiverPixelType`, `RiverSegment`, `RiverMapService`, and `RiverMeshService` into `Terrain.Rivers`.
- Added `IRiverTerrainHeightSource`.
- Changed `RiverMeshService` from `TerrainManager?` to `IRiverTerrainHeightSource?`.
- Made editor `TerrainManager` implement `IRiverTerrainHeightSource` explicitly.

**Architecture Compliance:**
- Runtime river mesh generation no longer references editor services.
- Editor still owns authoring lifecycle and only supplies a height source.

### 2. Moved River Rendering Core and Shaders
**Files Changed:** `Terrain/Rendering/River/*`, `Terrain/Effects/River/*`, project/package files

**Implementation:**
- Moved `RiverComponent`, `RiverProcessor`, `RiverRenderObject`, `RiverRenderFeature`, resources/settings/data/vertex types to `Terrain.Rendering.River`.
- Added `RiverRenderGroups` so render group constants are runtime-owned.
- Moved River SDSL files and generated key files to `Terrain/Effects/River`.
- Updated shader namespaces from `Terrain.Editor` to `Terrain`.
- Added River shader key metadata to `Terrain/Terrain.csproj`.
- Removed River shader key metadata from `Terrain.Editor/Terrain.Editor.csproj`.
- Moved `reflection-specular` asset to `Terrain/Assets/River/Environment` and rooted it in `Terrain/Terrain.sdpkg`.

### 3. Updated Tests and Documentation
**Files Changed:** `Terrain.Editor.Tests/*`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- Updated River tests to import `Terrain.Rivers` and `Terrain.Rendering.River`.
- Updated text tests to read shader/render files from runtime paths.
- Updated shader compile tests to include `Terrain/Effects/River`.
- Updated docs to reflect runtime ownership of river core/shaders/assets.

---

## Problems Encountered & Solutions

### Problem 1: Stride targets missing on outer multi-target project
**Symptom:** `dotnet msbuild Terrain/Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` failed with MSB4057.

**Root Cause:** The outer SDK multi-target invocation did not import the package targets.

**Solution:** Run the target with the declared inner framework:
```powershell
dotnet msbuild Terrain/Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
```

### Problem 2: Generated RiverStrideLighting key used `Vector4x4`
**Symptom:** `RiverStrideLighting.sdsl.cs` failed to compile after moving to `Terrain`.

**Root Cause:** `Terrain.Editor` had `GlobalUsings.cs` aliasing `Vector4x4` to `Stride.Core.Mathematics.Matrix`; `Terrain` did not.

**Solution:** Added `Terrain/GlobalUsings.cs` with the same alias.

### Problem 3: Height-source tests still depended on TerrainManager internals
**Symptom:** The smoothing height resample test returned zero-height samples.

**Root Cause:** `TerrainManager` now exposes height sampling through explicit `IRiverTerrainHeightSource`; the old reflection stub did not initialize all state required by `GetHeightAtPosition`.

**Solution:** Replaced those test stubs with a direct `IRiverTerrainHeightSource` stub.

---

## Code Quality Notes

### Testing
- Ran shader key update successfully with `/p:TargetFramework=net10.0-windows`.
- Ran `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`.
- Result: all Task 2+3 river domain/rendering/shader tests pass; remaining failures are Task 4+ runtime scene/compositor wiring:
  - `runtime scene contains river system component`
  - `runtime compositor registers river render feature`
- Ran `git diff --check`; no whitespace errors, only existing line-ending normalization warnings.

### Known Warnings
- Existing NuGet vulnerability warnings.
- Existing nullable / WinForms manifest / unused field warnings.

---

## Next Session

### Immediate Next Steps
1. Task 4+: add runtime RiverSystem scene/component and compositor registration.
2. Continue runtime bundle/resource wiring for river width/height/runtime mesh loading as planned.

### Docs to Read Before Next Session
- [ADR-014 river rendering architecture](../../../decisions/adr-014-river-rendering-architecture.md)
- This session log.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- River domain and mesh code is now runtime-owned under `Terrain.Rivers`.
- River rendering core is now runtime-owned under `Terrain.Rendering.River`.
- River SDSL lives under `Terrain/Effects/River` and uses `namespace Terrain`.
- `RiverRenderingService` remains editor façade only.
- Remaining test failures are expected until Task 4+ scene/compositor work.

**Gotchas for Next Session:**
- Use `/p:TargetFramework=net10.0-windows` when invoking Stride generated-file targets on `Terrain.csproj`.
- Do not reintroduce editor references into `Terrain`.
- `Terrain.Editor.Models` still exists for non-river editor models such as `EditorMode`; stale scans should distinguish those from old river imports.
