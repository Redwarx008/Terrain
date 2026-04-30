# Research: Current Texturing Pipeline

- **Query**: How the SplatMap/texturing pipeline works end-to-end in the Terrain project
- **Scope**: Internal
- **Date**: 2026-04-30

## Findings

### Files Found

#### Editor - Shader Files (.sdsl)

| File Path | Description |
|---|---|
| `Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl` | GPU Compute shader that generates IndexMap + WeightMap from ClimateMask + Biome rules |
| `Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl` | Editor pixel shader: samples IndexMap/WeightMap, does height-blended material mixing |
| `Terrain.Editor/Effects/EditorTerrainDisplacement.sdsl` | Editor vertex shader: height displacement + crack fix |
| `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl` | Shared height/index/weight map parameter declarations and sampling utilities (8 sliced textures) |
| `Terrain.Editor/Effects/EditorTerrainHeightStream.sdsl` | Stream declarations for editor terrain (not read, referenced by other shaders) |

#### Editor - Shader Key Files (auto-generated .sdsl.cs)

| File Path | Description |
|---|---|
| `Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl.cs` | Parameter keys for BuildSplatMap compute shader (ClimateMaskTexture, Biomes, Layers, Modifiers, OutputIndexMap, OutputWeightMap) |
| `Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl.cs` | Parameter keys for editor diffuse shader |
| `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl.cs` | Parameter keys for editor height parameters (slice textures, bounds) |

#### Runtime - Shader Files (.sdsl)

| File Path | Description |
|---|---|
| `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` | Runtime pixel shader: Texture2DArray-based IndexMap/WeightMap sampling with height blending |
| `Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl` | Runtime vertex shader: displacement + SplatInfo stream setup |
| `Terrain/Effects/Stream/TerrainHeightStream.sdsl` | Stream declarations: TerrainSliceIndex, TerrainPageLocalPos, TerrainSplatSliceIndex, TerrainSplatPageLocalPos |
| `Terrain/Effects/Stream/TerrainHeightParameters.sdsl` | Height sampling utilities (HeightmapArray-based, VT streaming) |
| `Terrain/Effects/Build/TerrainBuildLodLookup.sdsl` | LOD lookup table compute shader |
| `Terrain/Effects/Build/TerrainBuildLodMap.sdsl` | LOD map texture compute shader |
| `Terrain/Effects/Build/TerrainBuildNeighborMask.sdsl` | Neighbor LOD delta compute shader |

#### Editor - C# Core Services

| File Path | Description |
|---|---|
| `Terrain.Editor/Services/TerrainManager.cs` | Central coordinator: loads terrain, creates ClimateMask + MaterialIndexMap, manages project save/load, triggers rule regeneration |
| `Terrain.Editor/Services/ClimateRuleService.cs` | Singleton managing Biome definitions, ClimateRuleLayers, and BiomeModifiers (the rule stack) |
| `Terrain.Editor/Services/ClimateMask.cs` | R8 byte array at half-resolution storing per-texel climate ID |
| `Terrain.Editor/Services/ClimateEditor.cs` | Handles climate brush strokes on ClimateMask with probability-based soft edges |
| `Terrain.Editor/Services/MaterialIndexMap.cs` | RGBA8 pixel data: 4 indices + 4 weights per texel (CK3-style control map) |
| `Terrain.Editor/Services/PaintEditor.cs` | Manual paint brush orchestration: BeginStroke/ApplyStroke/EndStroke lifecycle |
| `Terrain.Editor/Services/PaintBrushCore.cs` | Shared paint logic: linear falloff, slope filtering, material index writing |
| `Terrain.Editor/Services/MaterialSlotManager.cs` | 256-slot material manager with GPU Texture2DArray (albedo+normal+properties) |
| `Terrain.Editor/Services/IPaintTool.cs` | PaintEditContext struct + IPaintTool interface (Strategy pattern) |

#### Editor - Rendering

| File Path | Description |
|---|---|
| `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | GPU resource owner: DetailIndexMapTextures[], DetailWeightMapTextures[], ClimateMaskTexture, BiomeBuffer, LayerBuffer, ModifierBuffer; upload logic |
| `Terrain.Editor/Rendering/EditorTerrainSplatMapComputeDispatcher.cs` | Dispatches EditorTerrainBuildSplatMap compute shader per dirty slice |
| `Terrain.Editor/Rendering/EditorTerrainRenderFeature.cs` | Root render feature: manages draw loop, syncs climate resources, dispatches SplatMap compute |
| `Terrain.Editor/Rendering/EditorTerrainProcessor.cs` | Entity processor: creates materials, binds all shader parameters (slices, textures, material arrays) |
| `Terrain.Editor/Services/Commands/PaintEditCommand.cs` | Undo/redo command for paint strokes |

#### Runtime - C# Core

| File Path | Description |
|---|---|
| `Terrain/Streaming/TerrainStreaming.cs` | Runtime streaming: TerrainFileReader, GpuVirtualTextureArray, TerrainStreamingManager (LRU eviction, IO thread) |
| `Terrain/Core/TerrainProcessor.cs` | Runtime processor: loads .terrain file, initializes GPU resources, binds shader params |
| `Terrain/Core/TerrainComponent.cs` | Runtime component: data path, config, internal state |
| `Terrain/Rendering/TerrainRenderObject.cs` | Runtime render mesh: holds GPU arrays (Heightmap, DetailIndexMap, DetailWeightMap) |
| `Terrain/Rendering/TerrainQuadTree.cs` | Runtime quadtree for LOD selection |
| `Terrain/Materials/RuntimeMaterialManager.cs` | Runtime material loading from TOML config, builds Texture2DArray |

#### Architecture Docs

| File Path | Description |
|---|---|
| `easysdd/architecture/shader-pipeline.md` | Shader flow: 3-stage compute, displacement, diffuse |
| `easysdd/architecture/climate-material.md` | ClimateMask to MaterialIndexMap data pipeline |
| `easysdd/architecture/editor-services.md` | Editor service architecture |
| `easysdd/architecture/brush-commands.md` | Brush command design |

### Code Patterns

#### 1. Two Parallel Texturing Paths: Editor vs Runtime

The project has completely separate editor and runtime rendering paths that share conceptual design but differ in GPU resource management:

- **Editor**: Uses up to 8 individual `Texture2D` slices for heightmaps, IndexMap, and WeightMap (bound as `HeightmapSlice0..7`, `IndexMapSlice0..7`, `WeightMapSlice0..7`). Climate-driven Compute Shader generates IndexMap/WeightMap on GPU.
- **Runtime**: Uses `Texture2DArray` with virtual texture streaming (LRU eviction, IO thread). No compute generation; textures are pre-baked in the `.terrain` file.

#### 2. Half-Resolution SplatMap (1/2 of Heightmap)

Both editor and runtime use SplatMap at half the heightmap resolution. Coordinate conversion is `splatXY = heightmapXY / 2`. This applies to:
- `ClimateMask`: half-res (aligned with SplatMap)
- `MaterialIndexMap`: half-res
- `DetailIndexMapTextures`/`DetailWeightMapTextures`: each slice is `(sliceWidth+1)/2` by `(sliceHeight+1)/2`
- Runtime file format: `TerrainFileHeader.SplatMapResolutionRatio = 2` for v3+ files

#### 3. ClimateMask + Rule-Based GPU Compute Generation

The editor generates material assignments entirely on GPU:

1. **ClimateMask** (`ClimateMask.cs`): R8 byte array, half-res. Each texel = one climate/biome ID (0-255).
2. **ClimateRuleService** (`ClimateRuleService.cs`): Manages `ClimateDefinition` (biomes), `ClimateRuleLayer` (layers per biome), `BiomeModifier` (modifier stack per layer).
3. **EditorTerrainBuildSplatMap** compute shader:
   - Reads ClimateMask to get `biomeId` per texel
   - Iterates layers matching `biomeId`
   - For each layer, evaluates modifier stack (HeightRange, SlopeRange, CurvatureRange, DirectionRange, Noise, TextureMask)
   - Tracks top-4 material indices and weights per texel
   - Outputs: `OutputIndexMap` (RGBA8: 4 material indices / 255) + `OutputWeightMap` (RGBA8: 4 blend weights normalized)
4. **EditorTerrainEntity.UploadBiomeBuffers**: Packs ClimateRuleService data into GPU StructuredBuffers (`BiomeGpu`, `LayerGpu`, `ModifierGpu`).

#### 4. Manual Paint (Direct Brush on MaterialIndexMap)

Alongside the rule-based generation, the editor supports direct brush painting:

- **PaintEditor**: Manages stroke lifecycle (BeginStroke/ApplyStroke/EndStroke)
- **IPaintTool** implementations: `PaintMaterialTool`, `EraseTool`
- **PaintBrushCore**: Applies brush with linear falloff and optional slope filter
- Operates directly on `MaterialIndexMap` (CPU-side byte array)
- Converts world coords to half-res splat coords for painting
- Marks dirty regions which get uploaded to GPU on next draw

#### 5. Height-Blended Material Sampling (Both Editor and Runtime)

The diffuse shaders (`EditorTerrainDiffuse.sdsl`, `MaterialTerrainDiffuse.sdsl`) use the same pattern:

1. Compute world-space normal from height differences
2. Sample 4 nearest IndexMap/WeightMap texels (bilinear interpolation)
3. Accumulate up to 4 unique material indices with their combined weights
4. Sample `MaterialDiffuseHeightArray` and `MaterialNormalArray` for each unique material
5. Apply **height-based blending** (not simple alpha blend): `CalcHeightBlendFactors` uses material height + control weight to determine blend, preventing visible seams at material boundaries

#### 6. Material Slot System

- **MaterialSlotManager** (editor): 256 slots, each with albedo/normal/properties texture paths. Builds GPU `Texture2DArray` on demand.
- **RuntimeMaterialManager** (runtime): Reads slot config from TOML, builds same arrays.

#### 7. Dirty Tracking and GPU Upload

Editor uses a `DirtyRegionTracker` per slice that supports:
- Per-channel dirty flags (Height, DetailIndex, DetailWeight)
- Region-based tracking (partial uploads) or full-slice dirty
- Climate-specific dirty: `ClimateSplatDirty` flag per slice, `climateMaskTextureDirty`, `climateRulesDirty`
- Upload pipeline: `EditorTerrainProcessor.Draw()` syncs Height and MaterialIndex channels; `EditorTerrainRenderFeature.Draw()` syncs ClimateMask + dispatches SplatMap compute

### Data Flow: Editor Brush to GPU Rendering

```
User Input (Brush Stroke)
    |
    v
ClimateEditor.ApplyStroke()  OR  PaintEditor.ApplyStroke()
    |                                    |
    v                                    v
ClimateMask.SetValue()         MaterialIndexMap.SetIndex()
    |                                    |
    v                                    v
TerrainManager.MarkClimateMaskDirty()   TerrainManager.MarkDataDirty(MaterialIndex)
    |                                    |
    v                                    v
EditorTerrainEntity.MarkClimateMaskDirty()  EditorTerrainEntity.MarkRegionDirty(DetailIndex)
EditorTerrainEntity.MarkClimateRulesDirty()
EditorTerrainEntity.MarkAllClimateSplatDirty()
    |                                    |
    v                                    v
[Frame Draw]                            [Frame Draw]
EditorTerrainProcessor.Draw():           EditorTerrainProcessor.Draw():
  SyncDataToGpu(MaterialIndex)            SyncDataToGpu(MaterialIndex)
    |                                    |
EditorTerrainRenderFeature.Draw():       (no compute dispatch needed -
  SyncClimateResourcesToGpu()             CPU-painted data goes direct to GPU)
  SplatMapComputeDispatcher.Dispatch()
    |                                    |
    v                                    v
GPU Compute:                             GPU Upload:
  ClimateMask + Rules -> IndexMap/WeightMap   MaterialIndexMap -> DetailIndexMapTextures
    |                                         MaterialIndexMap -> DetailWeightMapTextures
    v                                    |
GPU Pixel Shader (both paths merge):    v
  Sample IndexMap + WeightMap
  -> Sample Material Texture2DArrays
  -> Height-blended output
```

### Biome/Climate-Related Code

#### Existing Biome System (GPU Compute Path)

The biome system is already functional:

1. **ClimateDefinition** = Biome definition (ID, name, debug color)
2. **ClimateRuleLayer** = Layer within a biome (has `ClimateId`, `MaterialSlotIndex`, ordered by `PriorityOrder`, modifier stack)
3. **BiomeModifier** = Modifier within a layer (6 types: HeightRange, SlopeRange, CurvatureRange, DirectionRange, Noise, TextureMask; 5 blend modes: Multiply, Add, Subtract, Min, Max)
4. **ClimateEditor** = Brush for painting biome IDs onto ClimateMask
5. **EditorTerrainBuildSplatMap** = GPU compute that evaluates rules and outputs top-4 materials per texel

The naming is in transition: types still say "Climate" but the semantics have shifted to "Biome" (documented in ClimateRuleService header comment). The `ClimateRuleService` exposes both `Climates`/`Biomes` and `Rules`/`Layers` property aliases.

#### Key Structures Matching Between C# and GPU Shader

| C# Struct | GPU Struct | Fields |
|---|---|---|
| `ClimateDefinition` | `BiomeGpu` | BiomeId, LayerStartIndex, LayerCount, DebugColor |
| `ClimateRuleLayer` | `LayerGpu` | LayerId, BiomeId, MaterialSlotIndex, Enabled, Visible, PriorityOrder, ModifierStartIndex, ModifierCount |
| `BiomeModifier` | `ModifierGpu` | ModifierType, BlendMode, Enabled, TextureMaskChannel, Opacity, Min, Max, MinFalloff, MaxFalloff, Radius, AngleDegrees, AngleRangeDegrees, Scale, OffsetX, OffsetY, Seed, Octaves, Invert |

#### Top-4 Material Selection Algorithm (in shader)

The compute shader uses `PushTop4` to maintain the 4 highest-weight material indices per texel. This means:
- Each texel can blend at most 4 different materials
- The weight distribution is normalized after selection
- The index 255 is used as a sentinel for "no material" (since `materialIndex > 254` is rejected in AccumulateMaterial)

### Half-Resolution SplatMap Design Rationale

From the architecture doc and recent commits:
- SplatMap at 1/2 heightmap resolution prevents GPU texture overflow on large terrains (a 16385x16385 heightmap would need 8193x8193 splatmap textures per slice, which is feasible; full-res would be 16385x16385 which exceeds GPU limits)
- Coordinate mapping is consistent: `splatXY = heightmapXY / 2`
- ClimateMask is also half-res, aligned with SplatMap space

### .sdpkg Asset Definitions

| File Path | Description |
|---|---|
| `Terrain.Editor/Terrain.Editor.sdpkg` | Editor package: references editor shader assets |
| `Terrain/Terrain.sdpkg` | Runtime package: references runtime shader assets |
| `Terrain.Windows/Terrain.Windows.sdpkg` | Game entry point package |

### Related Specs

- `.trellis/spec/editor/state-management.md` -- Editor state management guidelines
- `.trellis/spec/guides/cross-layer-thinking-guide.md` -- Cross-layer data flow guidelines
- `.trellis/spec/runtime/error-handling.md` -- Error handling for TerrainComponent

## Caveats / Not Found

- The `Terrain.Editor/Effects/EditorTerrainDisplacement.sdsl` and `EditorTerrainHeightStream.sdsl` files were not read in full but are referenced by the rendering pipeline. They follow the same pattern as the runtime counterparts but use sliced textures instead of Texture2DArray.
- The `TextureMask` modifier type (type=5) exists in the BiomeModifier enum and GPU struct but the shader currently returns a hardcoded `1.0f` (or `0.0f` if inverted) for this type, meaning texture-mask-based modifiers are not yet fully implemented on the GPU side.
- The `ModifierGpu` struct has `Radius` and `Octaves` fields but they do not appear to be used in the current shader implementation (Noise uses `Scale` only with a simple 2D value noise).
- There is no direct "TerrainTexturePlugin" class in the codebase; the texturing system is composed of ClimateRuleService + MaterialSlotManager + the compute/fragment shader pipeline.
- The `PaintEditCommand.cs` file was found but not read in detail; it handles undo/redo for paint strokes.
- The climate-mask doc says it's 1/4 resolution, but the actual code uses 1/2 resolution (aligned with SplatMap). The doc may be outdated.
