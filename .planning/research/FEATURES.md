# Feature Landscape

**Domain:** Terrain Slot Editor (Standalone)
**Researched:** 2026-03-29

## Table Stakes

Features users expect. Missing = product feels incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Heightmap Editing - Raise/Lower** | Core terrain sculpting, every editor has this | Low | Basic brush that adds/subtracts height |
| **Heightmap Editing - Smooth** | Essential for natural-looking terrain | Low | Averages neighboring heights, removes jaggedness |
| **Heightmap Editing - Flatten** | Creates flat areas for gameplay (paths, platforms) | Low | Sets heights to target level |
| **Brush Size Control** | Users need varied scale of edits | Low | Slider or input field |
| **Brush Strength/Opacity** | Fine control over edit intensity | Low | Slider or input field |
| **Brush Falloff** | Soft edges on brushes are expected | Medium | Linear/smooth/constant options |
| **Circle Brush Shape** | Default brush shape in all editors | Low | Standard circular mask |
| **Undo/Redo** | Safety net for mistakes, expected in any editor | Medium | Configurable history depth |
| **Real-time 3D Preview** | WYSIWYG editing is baseline expectation | Medium | Reuse existing LOD rendering |
| **Camera Navigation** | Users must move around terrain to edit | Low | Orbit, pan, zoom controls |
| **Open/Save Heightmap** | Basic file I/O for any editor | Medium | PNG support (16-bit preferred) |
| **Material/Texture Painting** | Painting textures on terrain is expected | Medium | Splatmap-based material slots |
| **Brush Preview Cursor** | Shows where brush will affect | Low | Overlay in viewport |

## Differentiators

Features that set product apart. Not expected, but valued.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **R8 SplatMap (256 Materials)** | Supports far more materials than typical 4-8 layer systems | High | Requires custom shader support for bilinear blending |
| **Bilinear Material Blending** | Smooth transitions between material slots, not harsh edges | Medium | Interpolates between 4 nearest material indices |
| **Noise Perturbation on Blending** | Natural-looking material boundaries, not artificial smooth lines | Medium | Adds procedural noise to blend weights |
| **Noise Brush Shape** | Organic, non-uniform brush patterns | Low | Perlin or simplex noise-based mask |
| **Square Brush Shape** | Useful for architectural/structured edits | Low | Simple square mask |
| **Heightmap Noise Brush** | Adds terrain detail/variation | Medium | Procedural noise added to heights |
| **Material Slot Management** | Default material pack + custom import workflow | Medium | Preview thumbnails, drag-drop assignment |
| **Dual Export Format** | PNG for external tools, .terrain for direct runtime use | Medium | One-click export to both formats |
| **Configurable Undo History** | User controls memory vs history depth tradeoff | Low | Settings panel option |
| **Brush Import** | Custom brush shapes from images | Low | Load grayscale image as brush mask |
| **Heightmap Paging** | Edit massive terrains without memory issues | High | Critical for 4K+ heightmaps |

## Anti-Features

Features to explicitly NOT build.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Erosion Simulation** | Complex physics-based simulation, not core to slot editing | Mark as future consideration; focus on direct editing tools |
| **Procedural Generation** | Different product category (World Machine/Gaea territory) | Focus on manual painting/sculpting workflow |
| **Vegetation/Object Placement** | Already marked out-of-scope in PROJECT.md | GUI layout reserves space for future implementation |
| **Runtime Terrain Deformation** | Editor is offline tool, not game runtime feature | Keep editor separate from game runtime |
| **Multiplayer Collaboration** | Significant complexity for niche use case | Single-user desktop application |
| **Terrain Holes/Caves** | Requires mesh manipulation beyond heightmap | Could be future feature but not MVP |
| **Road/River Tools** | Specialized tools requiring pathfinding logic | Manual sculpting achieves similar results |
| **Auto-LOD Generation in Editor** | LOD is for export, not editing | LOD preview only, generation at export time |

## Feature Dependencies

```
Heightmap Editing (Raise/Lower/Smooth/Flatten)
    └── Brush System (Size/Strength/Falloff)
        └── Brush Shapes (Circle/Square/Noise)
            └── Brush Preview Cursor

Material Painting
    └── Material Slot Management
        └── R8 SplatMap Storage
            └── Bilinear Blending + Noise Perturbation

Undo/Redo System
    └── All Editing Operations
        └── Command Pattern (required for all edits)

Real-time 3D Preview
    └── Camera Navigation
    └── Existing LOD Rendering System (reuse)

File I/O
    └── Heightmap PNG Import
    └── SplatMap PNG Import/Export
    └── .terrain Export
        └── Mipmap Generation (export-time only)
```

## MVP Recommendation

Prioritize:
1. **Heightmap Editing (Raise/Lower, Smooth, Flatten)** - Core value proposition, table stakes
2. **Brush System (Size, Strength, Falloff, Circle shape)** - Required for any editing
3. **Undo/Redo** - Safety net, expected baseline
4. **Real-time 3D Preview + Camera Navigation** - WYSIWYG is non-negotiable

First Differentiator to Add:
5. **Material Painting with R8 SplatMap** - Unique value (256 materials), core to "Slot Editor" concept

Defer:
- **Noise Brush Shape**: Useful but not critical for MVP
- **Brush Import**: Nice-to-have, standard shapes cover most use cases
- **Heightmap Paging**: Implement when supporting large terrains (>4K)

## Competitive Analysis Summary

| Editor | Max Materials | Brush Types | Undo | Export Formats |
|--------|---------------|-------------|------|----------------|
| Unity Terrain | 4-8 per pass | Circle, Square, Custom | Yes | TerrainData, Heightmap RAW |
| Unreal Landscape | 8 per pass | Circle, Square, Custom | Yes | Heightmap, .umap |
| World Creator | Unlimited | Circle, Square, Stamp | Yes | Multiple (Unity, UE, PNG) |
| Gaea | Node-based | Procedural brushes | Yes | Multiple formats |
| **This Editor** | **256 (R8)** | Circle, Square, Noise | Yes | PNG, .terrain |

## Implementation Complexity Assessment

| Feature Category | Est. Effort | Risk Level | Notes |
|------------------|-------------|------------|-------|
| Height Editing Tools | 2-3 days | Low | Well-understood algorithms |
| Brush System | 2-3 days | Low | Standard falloff calculations |
| Undo/Redo | 2-3 days | Medium | Region-based snapshots for memory efficiency |
| Material Painting | 3-5 days | Medium | R8 splatmap is unique, needs shader work |
| File I/O | 2-3 days | Low | PNG well-supported, .terrain already exists |
| Camera Navigation | 1-2 days | Low | Standard orbit/pan/zoom |
| Brush Preview | 1 day | Low | Simple overlay |

## Sources

- Unity Terrain Documentation: https://docs.unity3d.com/Manual/terrain-Sculpt.html (verified 2026-03-29)
- Unity Terrain Settings: https://docs.unity3d.com/Manual/terrain-OtherSettings.html (verified 2026-03-29)
- World Creator Features: https://www.world-creator.com/ (verified 2026-03-29)
- Gaea Terrain Editor: https://quadspinner.com/gaea/ (verified 2026-03-29)
- Existing codebase analysis: Terrain.Editor/, Terrain/Rendering/, TerrainPreProcessor/
- PROJECT.md requirements analysis

---

*Feature research: 2026-03-29*
