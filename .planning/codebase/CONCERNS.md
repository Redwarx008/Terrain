# Codebase Concerns

**Analysis Date:** 2026-03-27

## Tech Debt

### Editor UI - Incomplete Feature Stubs

**Issue:** The Terrain.Editor project contains extensive TODO stubs for core editor functionality. Menu items and context menu actions are defined but not implemented.

**Files:**
- `Terrain.Editor/UI/MainWindow.cs` (lines 279-406)
- `Terrain.Editor/UI/Panels/HierarchyPanel.cs` (lines 243-280, 323)
- `Terrain.Editor/UI/Panels/AssetsPanel.cs` (lines 453-505)
- `Terrain.Editor/UI/Panels/InspectorPanel.cs` (lines 100, 296-303)
- `Terrain.Editor/UI/Panels/SceneViewPanel.cs` (line 278)
- `Terrain.Editor/UI/Panels/ConsolePanel.cs` (line 345)

**Impact:** Editor is non-functional for actual asset manipulation. Users cannot create/save projects, import assets, or perform standard editor operations.

**Fix approach:** Implement the stubbed functionality or remove menu items until features are ready. Priority order:
1. File operations (New, Open, Save)
2. Asset import (textures, models)
3. Scene object manipulation (Create, Delete, Rename)

### Reflection-Based Stride Integration

**Issue:** `TerrainRenderFeature` uses reflection to access internal Stride APIs for sub-render feature binding.

**File:** `Terrain/Rendering/TerrainRenderFeature.cs` (lines 42-44, 505-521)

```csharp
private static readonly MethodInfo? AttachRootRenderFeatureMethod = typeof(SubRenderFeature).GetMethod("AttachRootRenderFeature", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
private static readonly FieldInfo? RootRenderFeatureField = typeof(SubRenderFeature).GetField("RootRenderFeature", BindingFlags.Instance | BindingFlags.NonPublic);
private static readonly PropertyInfo? RenderSystemProperty = typeof(RenderFeature).GetProperty("RenderSystem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
```

**Impact:** Fragile dependency on Stride internals. Breaking changes in Stride updates will cause runtime failures.

**Fix approach:** Monitor Stride API evolution. If public APIs become available, migrate immediately. Add unit tests to detect breakage early.

### Wireframe Mode Controller - Complex State Management

**Issue:** `TerrainWireframeModeController` maintains complex state tracking for render stage bindings with manual lifecycle management.

**File:** `Terrain/Rendering/TerrainWireframeModeController.cs` (lines 44-107)

**Impact:** Risk of memory leaks or stale references if GraphicsCompositor changes at runtime. Complex null-checking pattern suggests architectural tension.

**Fix approach:** Consider implementing as a proper Stride render stage or using disposable pattern more explicitly.

## Known Bugs

### Buffer Pool Exhaustion Warning

**Issue:** `TerrainStreamingManager` logs buffer pool exhaustion once but does not provide ongoing visibility.

**File:** `Terrain/Streaming/TerrainStreaming.cs` (lines 701-710)

```csharp
if (!hasLoggedBufferPoolExhaustion)
{
    Log.Warning("Terrain streaming buffer pool is exhausted; deferring page request until a buffer is returned.");
    hasLoggedBufferPoolExhaustion = true;
}
```

**Impact:** Once exhausted, subsequent failures are silent. Difficult to diagnose persistent streaming issues.

**Fix approach:** Add periodic re-logging or metrics export for buffer pool pressure.

### Editor UI - No Input Validation

**Issue:** `InspectorPanel` property editors do not validate input ranges or types.

**File:** `Terrain.Editor/UI/Panels/InspectorPanel.cs` (lines 194-317)

**Impact:** Invalid values can be entered without feedback, potentially causing runtime errors when applied to terrain components.

**Fix approach:** Add validation callbacks to `Property` class and display error feedback in UI.

## Security Considerations

### File Path Traversal

**Issue:** `TerrainProcessor.ResolveTerrainDataPath` does not sanitize input paths beyond basic trimming.

**File:** `Terrain/Core/TerrainProcessor.cs` (lines 370-377)

```csharp
private static string ResolveTerrainDataPath(string terrainDataPath)
{
    terrainDataPath = terrainDataPath.Trim().Trim('"');
    string fullPath = Path.IsPathRooted(terrainDataPath)
        ? terrainDataPath
        : Path.Combine(AppContext.BaseDirectory, terrainDataPath);
    return Path.GetFullPath(fullPath);
}
```

**Impact:** Potential path traversal if user-controlled input is passed directly. Could allow reading arbitrary files.

**Current mitigation:** File existence check and `.terrain` extension validation in `ValidateTerrainDataPath`.

**Recommendations:** Add explicit path traversal detection. Ensure terrain data paths are never user-controllable in networked scenarios.

### Native Memory Management

**Issue:** `PageBufferAllocator` uses `NativeMemory.Alloc` without zero-initialization.

**File:** `Terrain/Streaming/PageBufferAllocator.cs` (lines 75-90)

**Impact:** Potential information disclosure if uninitialized memory contains sensitive data from previous allocations.

**Current mitigation:** Memory is overwritten with terrain height data immediately after allocation.

**Recommendations:** Consider using `NativeMemory.AllocZeroed` if security context requires it.

## Performance Bottlenecks

### QuadTree Selection - Single-Threaded

**Issue:** `TerrainQuadTree.Select` runs entirely on the calling thread with no parallelization.

**File:** `Terrain/Rendering/TerrainQuadTree.cs` (lines 54-99)

**Impact:** For large terrains with many LOD levels, selection can become CPU-bound. The recursive `SelectNode` calls process chunks sequentially.

**Improvement path:** Consider parallelizing top-level chunk selection or implementing incremental/approximate selection for distant chunks.

### MinMaxErrorMap Generation - High Memory Pressure

**Issue:** `MinMaxErrorMap.GenerateInternal` allocates large float arrays for entire heightmap.

**File:** `TerrainPreProcessor/Models/MinMaxErrorMap.cs` (lines 89-101)

```csharp
float[] rawHeights = new float[mapWidth * mapHeight];
```

**Impact:** For large heightmaps (e.g., 16k x 16k), this requires ~1GB of contiguous memory just for the raw height array.

**Improvement path:** Process heightmap in tiles or use memory-mapped files for large inputs.

### Streaming Upload Queue - No Prioritization

**Issue:** `TerrainStreamingManager` processes upload requests in FIFO order without considering view priority.

**File:** `Terrain/Streaming/TerrainStreaming.cs` (lines 615-641)

**Impact:** Distant chunks may be uploaded before nearby chunks when camera moves rapidly, causing visible popping.

**Improvement path:** Implement priority queue based on distance to camera or view frustum intersection.

## Fragile Areas

### TerrainRenderFeature Sub-Render Feature Lifecycle

**Files:** `Terrain/Rendering/TerrainRenderFeature.cs` (lines 272-325, 483-503)

**Why fragile:** Complex collection change handling with `rebuildingManagedRenderFeatures` flag to prevent re-entrancy issues. The managed/unmanaged feature distinction is subtle and error-prone.

**Safe modification:** Always test with both default and custom sub-render feature configurations. Verify no double-initialization or disposal occurs.

**Test coverage:** No automated tests detected for render feature lifecycle.

### GPU Resource Reinitialization

**File:** `Terrain/Core/TerrainProcessor.cs` (lines 161-222)

**Why fragile:** `ApplyLoadedTerrainData` disposes and recreates all GPU resources when terrain data changes. Race conditions possible if render thread is mid-draw.

**Safe modification:** Ensure all GPU resource updates happen during `Draw()` phase, never during `Update()`. The current implementation follows this pattern but relies on Stride's synchronization.

**Test coverage:** No tests for rapid terrain data switching.

### ImGui Control Event System

**File:** `Terrain.Editor/UI/Controls/ControlBase.cs` (lines 405-431)

**Why fragile:** Input event bubbling uses recursive `HandleInput` calls. Deep hierarchies may cause stack issues or event swallowing bugs.

**Safe modification:** Keep control hierarchies shallow. Add maximum depth limit for event propagation.

**Test coverage:** No UI automation tests detected.

## Scaling Limits

### MaxResidentChunks Hard Limit

**Current capacity:** Default 1024 chunks in `TerrainComponent.MaxResidentChunks`.

**Limit:** Each chunk requires GPU texture array slice. Hardware limit on texture array size (typically 2048 on modern GPUs).

**Scaling path:** Implement virtual texturing with page cache eviction or increase texture array size (requires hardware support verification).

### Chunk Node Buffer Capacity

**Current capacity:** `MaxVisibleChunkInstances` default 65536.

**File:** `Terrain/Core/TerrainProcessor.cs` (lines 323-334)

**Limit:** Single structured buffer allocation. For massive terrains with high detail, this may be insufficient.

**Scaling path:** Implement multi-pass rendering or sparse buffer allocation.

### Terrain File Format Version

**Current version:** Version 1 in `TerrainFileHeader.SupportedVersion`.

**File:** `Terrain/Streaming/TerrainStreaming.cs` (lines 102-123)

**Limit:** No forward-compatibility mechanism. Future format changes will break existing files.

**Scaling path:** Design version migration strategy before releasing v2 format.

## Dependencies at Risk

### Stride 4.3.0.2507

**Risk:** Reflection-based APIs may change in Stride 4.4+.

**Impact:** `TerrainRenderFeature` binding logic would break.

**Migration plan:** Track Stride changelog for public API alternatives. Consider contributing upstream patches to expose required APIs publicly.

### ImGui.NET 1.90.0.1

**Risk:** Stride.CommunityToolkit.ImGui depends on specific ImGui.NET version.

**Impact:** Version mismatches cause ABI compatibility issues.

**Migration plan:** Pin versions explicitly. Test thoroughly before upgrading.

### SixLabors.ImageSharp (TerrainPreProcessor)

**Risk:** ImageSharp has licensing considerations for commercial use.

**Impact:** Legal risk if project is commercialized.

**Migration plan:** Evaluate license compatibility. Consider System.Drawing.Common or SkiaSharp as alternatives if needed.

## Missing Critical Features

### Terrain Editing Runtime

**Problem:** No runtime terrain modification API exists. Heightmap is read-only after loading.

**Blocks:** Real-time terrain editing, deformation tools, procedural generation at runtime.

**Priority:** High for editor functionality.

### Physics Integration

**Problem:** No collision mesh generation or physics integration detected.

**Blocks:** Characters walking on terrain, physics objects interacting with terrain.

**Priority:** High for gameplay functionality.

### Splatmap Rendering

**Problem:** Splatmap data is written to `.terrain` file but not used in rendering.

**File:** `Terrain/Streaming/TerrainStreaming.cs` (lines 116-123) - `HasSplatMap` field exists but no runtime usage.

**Blocks:** Multi-texture terrain rendering.

**Priority:** Medium for visual quality.

### Async Terrain Loading

**Problem:** `TerrainProcessor.TryLoadTerrainData` is synchronous.

**File:** `Terrain/Core/TerrainProcessor.cs` (lines 119-159)

**Blocks:** Non-blocking terrain loading for large worlds. UI freezing during load.

**Priority:** Medium for user experience.

## Test Coverage Gaps

### Streaming System

**What's not tested:** `TerrainStreamingManager` IO thread behavior, error recovery, buffer pool exhaustion scenarios.

**Files:** `Terrain/Streaming/TerrainStreaming.cs` (lines 538-766)

**Risk:** Race conditions in multi-threaded streaming may cause crashes or memory corruption.

**Priority:** High - streaming is complex concurrent code.

### Compute Shader Dispatch

**What's not tested:** `TerrainComputeDispatcher` compute shader execution, resource barrier correctness.

**File:** `Terrain/Rendering/TerrainComputeDispatcher.cs`

**Risk:** GPU synchronization issues may cause visual corruption or device removal.

**Priority:** Medium - difficult to test without GPU infrastructure.

### MinMaxErrorMap Validation

**What's not tested:** Edge cases in geometric error calculation, very large heightmaps, malformed input handling.

**File:** `TerrainPreProcessor/Models/MinMaxErrorMap.cs`

**Risk:** Incorrect LOD selection or crashes with production heightmap data.

**Priority:** Medium - affects visual quality and stability.

### Editor UI Components

**What's not tested:** All custom ImGui controls, layout management, panel interactions.

**Files:** `Terrain.Editor/UI/**/*.cs`

**Risk:** UI regressions undetected until manual testing.

**Priority:** Low - editor is not production-critical.

---

*Concerns audit: 2026-03-27*
