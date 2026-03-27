# Architecture

**Analysis Date:** 2026-03-27

## Pattern Overview

**Overall:** Entity-Component-System (ECS) with Custom Render Pipeline

**Key Characteristics:**
- Built on Stride Game Engine 4.3.0.2507 (.NET 10)
- GPU-driven terrain rendering with compute shader-based LOD management
- Virtual Texture (VT) streaming system for heightmap data
- Quadtree-based chunk selection with screen-space error metrics
- Multi-project solution: Core Library, Windows Runtime, Editor, PreProcessor

## Layers

**Core Terrain Layer:**
- Purpose: Terrain component definition, data structures, and runtime file I/O
- Location: `Terrain/Core/`
- Contains: `TerrainComponent.cs`, `TerrainProcessor.cs`
- Depends on: Stride.Engine, Stride.Rendering
- Used by: Terrain.Windows, Terrain.Editor

**Rendering Layer:**
- Purpose: Custom render feature, compute dispatchers, and GPU resource management
- Location: `Terrain/Rendering/`
- Contains: `TerrainRenderFeature.cs`, `TerrainRenderObject.cs`, `TerrainQuadTree.cs`, `TerrainComputeDispatcher.cs`
- Depends on: Stride.Rendering, Stride.Graphics
- Used by: Core Terrain Layer

**Streaming Layer:**
- Purpose: Asynchronous heightmap page streaming and GPU texture array management
- Location: `Terrain/Streaming/`
- Contains: `TerrainStreaming.cs`, `PageBufferAllocator.cs`
- Depends on: System.IO, System.Threading, Stride.Graphics
- Used by: TerrainQuadTree

**Shader Layer:**
- Purpose: HLSL compute and vertex/pixel shaders for terrain rendering
- Location: `Terrain/Effects/`
- Contains: Compute shaders (Build/), Material shaders (Material/), Stream shaders (Stream/)
- Depends on: Stride shader system
- Used by: TerrainRenderFeature

**Editor Layer:**
- Purpose: ImGui-based editor with dockable panels for terrain editing
- Location: `Terrain.Editor/`
- Contains: `EditorGame.cs`, `UI/` (Panels, Controls, Layout, Styling)
- Depends on: Terrain (core), ImGui.NET, Stride.CommunityToolkit.ImGui
- Used by: Editor executable

**PreProcessor Layer:**
- Purpose: Avalonia desktop tool for converting heightmaps to .terrain files
- Location: `TerrainPreProcessor/`
- Contains: Models, Services, ViewModels, Views
- Depends on: Avalonia, SixLabors.ImageSharp
- Used by: Standalone tool

## Data Flow

**Terrain Rendering Pipeline:**

1. **Initialization:** `TerrainProcessor.Draw()` calls `Initialize()` which loads `.terrain` file via `TerrainFileReader`
2. **Quadtree Selection:** `TerrainQuadTree.Select()` traverses LOD hierarchy based on camera frustum and screen-space error
3. **Chunk Node Update:** Selected chunks written to `TerrainChunkNode[]` buffer and uploaded to GPU via `ChunkNodeBuffer`
4. **Compute Dispatch:** `TerrainComputeDispatcher.Dispatch()` runs three compute shaders:
   - `TerrainBuildLodLookup`: Builds LOD lookup table from chunk nodes
   - `TerrainBuildLodMap`: Generates LOD map texture for neighbor detection
   - `TerrainBuildNeighborMask`: Computes crack-fixing neighbor masks per instance
5. **Render:** `TerrainRenderFeature.Draw()` binds resources and issues `DrawIndexedInstanced()` with GPU-driven instance data

**Streaming Pipeline:**

1. **Request:** `TerrainStreamingManager.RequestChunk()` queues chunk requests from quadtree
2. **Async I/O:** Background thread reads heightmap pages from `.terrain` file via `RandomAccess.Read()`
3. **Upload:** `ProcessPendingUploads()` copies completed reads to GPU texture array slices
4. **LRU Eviction:** `GpuHeightArray` manages resident pages with LRU eviction and pinning support

**File Format (.terrain):**

```
[Header: TerrainFileHeader] → [MinMaxErrorMap Count] → [MinMaxErrorMap Data...] →
[VTHeader] → [Heightmap Mip Tiles...] → [Optional SplatMap VT Data]
```

## Key Abstractions

**TerrainComponent:**
- Purpose: Entity component holding terrain configuration and runtime state
- Location: `Terrain/Core/TerrainComponent.cs`
- Pattern: Stride EntityComponent with `[DataContract]` for serialization
- Key Properties: `TerrainDataPath`, `HeightScale`, `MaxScreenSpaceErrorPixels`, `MaxVisibleChunkInstances`

**TerrainRenderFeature:**
- Purpose: Root render feature integrating with Stride's rendering pipeline
- Location: `Terrain/Rendering/TerrainRenderFeature.cs`
- Pattern: Extends `RootEffectRenderFeature`, manages sub-render-features
- Key Behaviors: Clones MeshRenderFeature lighting config, proxies shadow maps

**TerrainQuadTree:**
- Purpose: Hierarchical chunk selection with LOD determination
- Location: `Terrain/Rendering/TerrainQuadTree.cs`
- Pattern: Recursive spatial subdivision with screen-space error metrics
- Key Method: `Select()` returns (renderCount, nodeCount) for GPU buffers

**TerrainStreamingManager:**
- Purpose: Async I/O and GPU upload coordination
- Location: `Terrain/Streaming/TerrainStreaming.cs`
- Pattern: Producer-consumer with `BlockingCollection`, background thread
- Key Types: `TerrainChunkKey` (LOD+XY), `TerrainPageKey` (Mip+XY)

**GpuHeightArray:**
- Purpose: GPU texture array slice management with LRU eviction
- Location: `Terrain/Streaming/TerrainStreaming.cs`
- Pattern: Dictionary mapping + LinkedList LRU + array pool
- Key Methods: `TryGetResidentSlice()`, `UploadPage()`, `TryEvictLeastRecentlyUsed()`

## Entry Points

**Terrain.Windows (Runtime):**
- Location: `Terrain.Windows/` (thin executable)
- Triggers: Standard Stride game bootstrap
- Responsibilities: Hosts Terrain library, provides game runtime

**Terrain.Editor (Editor):**
- Location: `Terrain.Editor/Program.cs` → `EditorGame`
- Triggers: Editor executable launch
- Responsibilities: Initializes ImGui UI, dockable panels, scene editing

**TerrainPreProcessor (Tool):**
- Location: `TerrainPreProcessor/Program.cs`
- Triggers: Avalonia desktop app startup
- Responsibilities: Heightmap → .terrain conversion with progress UI

## Error Handling

**Strategy:** Defensive validation with early returns and logging

**Patterns:**
- Null checks on GPU resources before use (e.g., `IsGpuDataValid()`)
- Try-pattern methods: `TryLoadTerrainData()`, `TryGetResidentPageForChunk()`
- Exception catching at I/O boundaries with user-facing messages
- CancellationToken support for streaming thread shutdown

## Cross-Cutting Concerns

**Logging:** Stride's `GlobalLogger` with "Quantum" category

**Validation:** File header magic/version checks, dimension validation in `TerrainFileReader`

**Resource Management:** Explicit `Dispose()` pattern for GPU resources, `SafeFileHandle` for I/O

**Thread Safety:** `ConcurrentDictionary` for queued keys, `BlockingCollection` for async I/O, `Interlocked` not used (single producer/consumer)

---

*Architecture analysis: 2026-03-27*
