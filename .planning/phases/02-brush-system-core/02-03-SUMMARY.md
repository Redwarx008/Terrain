---
phase: 02-brush-system-core
plan: 03
subsystem: brush-preview
tags: [raycasting, terrain-projection, height-cache, PREV-05]
dependencies:
  requires: [02-01, 02-02]
  provides: [terrain-projected-brush-preview]
  affects: [SceneViewPanel, TerrainManager, TerrainRaycast]
tech_stack:
  added: [ImageSharp-L16-loading, ray-plane-intersection, iterative-height-refinement]
  patterns: [nearest-neighbor-sampling, cpu-height-cache, screen-to-world-ray]
key_files:
  created:
    - path: Terrain.Editor/Services/TerrainRaycast.cs
      lines: 174
      purpose: Raycasting utilities for screen-to-world and terrain intersection
  modified:
    - path: Terrain.Editor/Services/TerrainManager.cs
      lines_added: 92
      purpose: CPU-side height data cache for terrain queries
    - path: Terrain.Editor/UI/Panels/SceneViewPanel.cs
      lines_added: 161
      lines_removed: 15
      purpose: Terrain-projected brush preview rendering
decisions:
  - id: D-03-01
    choice: Nearest-neighbor sampling for GetHeightAtPosition
    rationale: Matches shader's SampleHeightAtLocalPos behavior (no interpolation)
    alternatives_rejected: [bilinear-interpolation]
  - id: D-03-02
    choice: CPU-side height cache loaded from PNG
    rationale: Avoids GPU-CPU sync stalls during editing; PNG is source of truth
    alternatives_rejected: [readback-from-GPU]
metrics:
  duration: 15min
  tasks_completed: 3
  files_changed: 3
  commits: 3
  completed_date: 2026-03-31
---

# Phase 02 Plan 03: Terrain Projected Brush Preview Summary

**One-liner:** Upgraded 2D screen-space brush preview to 3D terrain-projected preview with ray-terrain intersection and CPU height cache.

## Objective

Upgrade the existing 2D screen-space brush preview to a 3D decal preview projected onto the terrain surface, allowing users to accurately see the brush's actual area of effect on uneven terrain.

## Tasks Completed

| Task | Name | Status | Commit |
|------|------|--------|--------|
| 02-03-01 | Create TerrainRaycast.cs | Done | 1523e26 |
| 02-03-02 | Add CPU height cache to TerrainManager | Done | 24b967b |
| 02-03-03 | Update SceneViewPanel RenderBrushPreview | Done | 1f159d9 |

## Implementation Details

### Task 02-03-01: TerrainRaycast Utility Class

Created `TerrainRaycast.cs` with three static methods:

- **ScreenToWorldRay**: Converts screen coordinates to a world-space ray using inverse view-projection matrix transformation. Handles NDC conversion and perspective divide.

- **RayPlaneIntersection**: Calculates ray-plane intersection for an infinite plane. Used as initial guess for terrain intersection.

- **RayTerrainIntersection**: Iteratively refines terrain intersection using height queries. Starts from average terrain height plane and converges to surface within 20 iterations.

### Task 02-03-02: CPU Height Data Cache

Extended `TerrainManager.cs`:

- Added `heightDataCache` (ushort[]) for storing L16 heightmap pixel data
- Added `GetHeightAtPosition(float x, float z)` using **nearest-neighbor sampling** (matches shader behavior)
- Added `IsPositionOnTerrain(float x, float z)` for bounds checking
- Added `LoadHeightDataCache(string path)` using ImageSharp's `ProcessPixelRows` API
- Height cache is loaded when terrain is loaded and cleared on removal/disposal

### Task 02-03-03: Terrain-Projected Brush Preview

Updated `SceneViewPanel.cs`:

- Replaced 2D screen-space circles with 3D terrain-projected preview
- Added `GetActiveCamera()` to access camera from camera controller
- Added `GenerateWorldSpaceCircle()` to create terrain-following circle points
- Added `WorldToScreen()` for 3D-to-2D projection with behind-camera check
- Added `DrawProjectedCircle()` and `DrawProjectedCircleFilled()` for rendering

The preview now:
- Projects onto terrain surface at mouse position
- Follows terrain height contours (visible on slopes/hills)
- Shows outer circle for brush extent
- Shows inner filled circle for 100% strength area (based on EffectiveFalloff)
- Hides when no terrain intersection or terrain not loaded

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] MathF type resolution**

- **Found during:** Task 01
- **Issue:** Plan used `Math.Abs()` which doesn't exist for float types; needed `MathF` with System namespace
- **Fix:** Changed to `MathF.Abs()` and `MathF.Max()`, added `using System;`
- **Files modified:** TerrainRaycast.cs
- **Commit:** 1523e26

**2. [Rule 3 - Blocking] Image/Color type ambiguity**

- **Found during:** Task 02
- **Issue:** Adding ImageSharp using caused `Image` and `Color` to be ambiguous with Stride types
- **Fix:** Added type aliases `HeightmapImage` and `StrideColor` to disambiguate
- **Files modified:** TerrainManager.cs
- **Commit:** 24b967b

**3. [Rule 3 - Blocking] ImageSharp pixel data access**

- **Found during:** Task 02
- **Issue:** Plan's `image.CopyPixelDataTo(heightDataCache)` doesn't work with ushort[] in ImageSharp 3.x
- **Fix:** Used `ProcessPixelRows` API with manual copy of L16.PackedValue
- **Files modified:** TerrainManager.cs
- **Commit:** 24b967b

**4. [Rule 3 - Blocking] Vector3/Vector4 type ambiguity**

- **Found during:** Task 03
- **Issue:** Both Stride.Core.Mathematics and System.Numerics have Vector3/Vector4 types
- **Fix:** Added `StrideVector3` and `StrideVector4` type aliases
- **Files modified:** SceneViewPanel.cs
- **Commit:** 1f159d9

**5. [Rule 3 - Blocking] AddConvexPolyFilled ref parameter**

- **Found during:** Task 03
- **Issue:** `ref screenPoints[0]` doesn't work with List indexer (returns copy)
- **Fix:** Convert to array before passing: `screenPoints.ToArray()` then use `ref array[0]`
- **Files modified:** SceneViewPanel.cs
- **Commit:** 1f159d9

## Key Decisions

1. **Nearest-neighbor sampling**: Used for `GetHeightAtPosition` to match shader's `SampleHeightAtLocalPos` behavior without bilinear interpolation.

2. **CPU height cache from PNG**: Loaded directly from source PNG rather than GPU readback to avoid synchronization stalls during editing.

3. **Iterative ray-terrain intersection**: Uses 20 iterations maximum with height difference feedback for convergence on sloped terrain.

## Verification

- Build succeeded with no errors
- All 3 tasks committed individually
- Brush preview now projects onto terrain surface
- Preview follows terrain contours
- Size matches world units
- Falloff reflected in inner/outer circles

## Requirements Delivered

- PREV-05 (enhanced): Brush preview cursor in viewport - now projected onto terrain surface
