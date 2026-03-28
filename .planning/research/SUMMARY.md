# Research Summary: Terrain Slot Editor

**Project:** Terrain Slot Editor (Standalone)
**Synthesized:** 2026-03-29

---

## Executive Summary

The Terrain Slot Editor is a standalone desktop application for sculpting heightmaps and painting material slots on terrain. Built on the existing Stride Engine codebase with ImGui.NET for UI, it leverages substantial prior investment in terrain rendering, LOD systems, and UI framework. The key differentiator is R8 SplatMap support for 256 material slots with bilinear blending, far exceeding typical terrain editors that support 4-8 materials.

The recommended approach is a layered architecture with Command pattern for all edits, paged data management for large heightmaps, and CPU-side source-of-truth to avoid GPU synchronization pitfalls. The existing Terrain.Editor project already has the UI shell and panel infrastructure; the work is primarily implementing the editing logic, brush system, and undo/redo infrastructure.

Critical risks include GPU-CPU synchronization stalls during readback, undo memory explosion with naive implementations, and splatmap bilinear filtering artifacts that produce invalid material blends. Each has well-understood mitigation strategies documented in the pitfalls research.

---

## Key Findings

### From STACK.md

**Core Technologies:**
- **Stride Engine 4.3.0.2507** - Already integrated; provides DirectX 12/Vulkan rendering, scene management, input handling
- **ImGui.NET 1.91.6.1** - Already working via Stride.CommunityToolkit.ImGui; use for all editor UI
- **SixLabors.ImageSharp 3.1.12** - Already used in TerrainPreProcessor; handles PNG heightmap/splatmap I/O
- **Custom Command Pattern** - No external library; implement IUndoableCommand interface for terrain-specific undo

**Critical Version Notes:**
- Stride 4.3 requires .NET 10.0
- Use Hexa.NET.ImGui via toolkit only; do not mix with cimgui.NET directly
- No new packages required; all dependencies already in project

### From FEATURES.md

**Must-Have (Table Stakes):**
1. Heightmap Editing (Raise/Lower, Smooth, Flatten)
2. Brush System (Size, Strength, Falloff, Circle shape)
3. Undo/Redo with configurable history
4. Real-time 3D Preview with Camera Navigation
5. Open/Save Heightmap (PNG, 16-bit preferred)
6. Material/Texture Painting
7. Brush Preview Cursor

**Key Differentiator:**
- **R8 SplatMap with 256 Materials** - Far exceeds Unity (4-8) and Unreal (8) per-pass limits
- **Bilinear Material Blending + Noise Perturbation** - Smooth, natural material transitions

**Defer to v2+:**
- Noise Brush Shape
- Brush Import (custom shapes from images)
- Heightmap Paging (for 4K+ terrains)

### From ARCHITECTURE.md

**Major Components:**
1. **TerrainDocument** - Heightmap + SplatMap data, dirty state, file path (source of truth)
2. **HeightmapPageManager** - Large heightmap paging, resident pages, dirty tracking
3. **UndoManager** - Command history stack with region-based snapshots
4. **ToolController** - Active tool state, coordinates brush execution
5. **BrushController** - Brush parameters, shape computation, falloff calculation
6. **EditorModeManager** - Mode switching (Sculpt/Paint/Foliage)

**Key Patterns:**
- **Command Pattern** - All edits encapsulated as ICommand for undo/redo
- **Paged Data Management** - Fixed-size pages, LRU eviction for large terrains
- **Event-Driven UI** - Panels subscribe to controller/document events, never poll
- **Strategy Pattern for Brushes** - IBrushShape interface with multiple implementations

**Build Order:**
1. Foundation (ICommand, UndoManager, IBrushShape, TerrainDocument)
2. Data Layer (HeightmapPageManager, SplatMapManager, FileIOManager)
3. Controller Layer (EditorModeManager, BrushController, ToolController)
4. UI Integration (update existing panels)
5. Rendering Integration (connect to existing systems)

### From PITFALLS.md

**Top 5 Critical Pitfalls:**

| Pitfall | Prevention Strategy | Phase |
|---------|---------------------|-------|
| GPU-CPU Sync Stall | Maintain CPU-side copy as source of truth; never read back GPU during editing | Height Editing Core |
| Undo Memory Explosion | Delta-based compression (store modified region only); stroke batching | Undo/Redo System |
| LOD Inconsistency | Mark edited chunks dirty; force highest LOD temporarily; "Rebuild LOD Data" button | Height Editing Core |
| Splatmap Bilinear Artifacts | Nearest-neighbor sampling for splatmap; separate blend weight texture | Material Slot Painting |
| Heightmap Precision Loss | Keep intermediate calculations in float32; quantize only on final write | File I/O Core |

---

## Implications for Roadmap

### Suggested Phase Structure

**Phase 1: Foundation + Height Editing Core**
- **Rationale:** Establishes core data structures and the critical CPU-side source-of-truth pattern before any editing features
- **Delivers:** Command pattern, UndoManager, TerrainDocument, basic brush system, Raise/Lower/Smooth/Flatten tools
- **Features:** Heightmap Editing (table stakes), Brush System (Size/Strength/Falloff/Circle), Brush Preview Cursor
- **Pitfalls Addressed:** GPU-CPU Sync Stall, LOD Inconsistency, Heightmap Precision Loss

**Phase 2: File I/O + Real-time Preview**
- **Rationale:** Users can load/save and see results; enables iterative editing workflow
- **Delivers:** PNG heightmap import/export, .terrain export, camera navigation, 3D preview integration
- **Features:** Open/Save Heightmap, Real-time 3D Preview, Camera Navigation
- **Pitfalls Addressed:** Heightmap Precision Loss (export path)

**Phase 3: Undo/Redo System**
- **Rationale:** Safety net required before any serious editing; memory management critical
- **Delivers:** Full undo/redo with region-based snapshots, stroke batching, configurable history depth
- **Features:** Undo/Redo (table stakes), Configurable Undo History (differentiator)
- **Pitfalls Addressed:** Undo Memory Explosion

**Phase 4: Material Slot Painting**
- **Rationale:** Key differentiator; builds on established editing patterns
- **Delivers:** R8 SplatMap support, material slot management, bilinear blending, paint brush
- **Features:** Material/Texture Painting (table stakes), R8 SplatMap with 256 Materials, Bilinear Material Blending (differentiators)
- **Pitfalls Addressed:** Splatmap Bilinear Artifacts

**Phase 5: Enhanced Brush System**
- **Rationale:** Polish and differentiation after core functionality stable
- **Delivers:** Square brush, noise brush, brush import, noise perturbation on blending
- **Features:** Square Brush Shape, Noise Brush Shape, Brush Import (differentiators)

### Research Flags

**Needs `/gsd:research-phase` during planning:**
- Phase 4 (Material Slot Painting): R8 SplatMap bilinear blending is unique to this project; needs detailed shader research
- Phase 3 (Undo/Redo): Memory-efficient region snapshots need profiling data for target hardware

**Standard patterns (skip research):**
- Phase 1 (Foundation): Command pattern, brush falloff are well-documented
- Phase 2 (File I/O): PNG I/O via ImageSharp is straightforward

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All dependencies already integrated and working in existing codebase |
| Features | HIGH | Clear competitive analysis; table stakes well-defined |
| Architecture | HIGH | Existing codebase provides clear patterns to follow; panel infrastructure exists |
| Pitfalls | MEDIUM | GPU sync and undo memory are well-understood problems; splatmap blending is domain-specific and may need iteration |

### Gaps to Address

1. **Bilinear Blend Shader for R8 SplatMap** - No existing implementation to reference; needs prototyping
2. **MinMaxErrorMap Incremental Update** - Currently an offline preprocessing step; editor needs runtime solution
3. **Windows Native File Dialog** - P/Invoke or Windows Runtime projection needed; not yet tested
4. **Performance Baseline** - Target frame time budget during editing not yet established

---

## Sources

**STACK.md Sources:**
- NuGet.org package verification (2026-03-29)
- Existing project file analysis

**FEATURES.md Sources:**
- Unity Terrain Documentation (docs.unity3d.com)
- World Creator Features (world-creator.com)
- Gaea Terrain Editor (quadspinner.com)
- PROJECT.md requirements

**ARCHITECTURE.md Sources:**
- Existing codebase: Terrain.Editor/UI/, Terrain/Streaming/, TerrainPreProcessor/Services/
- Standard patterns: Command, Strategy, Event-Driven

**PITFALLS.md Sources:**
- Existing codebase: Terrain/Streaming/, Terrain/Rendering/, CONCERNS.md
- Domain knowledge: GPU pipeline synchronization, LOD systems, procedural noise

---

*Synthesis completed: 2026-03-29*
