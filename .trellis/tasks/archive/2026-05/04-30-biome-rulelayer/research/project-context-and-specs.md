# Research: Project Context and Specs

- **Query**: Understand the existing spec documents, project structure, biome/climate-related specs, .csproj layout, and recent journal entries for the terrain project
- **Scope**: internal
- **Date**: 2026-04-30

## Findings

### Project Structure

The project is a Stride engine terrain rendering and editing system with three main projects:

| Project | Path | Target | Description |
|---|---|---|---|
| Terrain (runtime) | `Terrain/Terrain.csproj` | net10.0-windows | Core library: terrain component, rendering, streaming, shaders |
| Terrain.Editor | `Terrain.Editor/` (no separate .csproj found in root -- bundled or implied) | net10.0-windows | Editor UI (Avalonia), services, compute dispatch, editor shaders |
| Terrain.Windows | `Terrain.Windows/Terrain.Windows.csproj` | net10.0-windows, win-x64 | Windows entry point, references Terrain.csproj |

**Solution**: `Terrain.sln` at project root.

**Key NuGet dependencies**: Stride.Engine, Stride.Video, Stride.Physics, Stride.Navigation, Stride.Particles, Stride.UI, Tommy (TOML library).

**Shared code**: `Shared/VirtualTextureLayout.cs` is linked into Terrain.csproj.

---

### Spec Documents (.trellis/spec/)

#### Guides (`.trellis/spec/guides/`)

| File | Description |
|---|---|
| `index.md` | Thinking guides index: Code Reuse and Cross-Layer thinking guides |
| `code-reuse-thinking-guide.md` | Pattern identification and duplication reduction |
| `cross-layer-thinking-guide.md` | Data flow across layers (API, Service, Component, Database) |

#### Runtime Specs (`.trellis/spec/runtime/`)

| File | Description |
|---|---|
| `index.md` | Runtime guidelines index -- core library conventions (nullable, DataContract, sealed, _camelCase) |
| `directory-structure.md` | Module organization: Terrain/, Terrain.Editor/, Terrain.Windows/, Shared/ |
| `database-guidelines.md` | TOML config (Tommy lib), binary terrain data, Stride asset system |
| `error-handling.md` | Exception patterns, error recovery |
| `quality-guidelines.md` | Code standards, #nullable enable, DataContract, review checklist, no TODO |
| `logging-guidelines.md` | Debug logging, log levels |

#### Editor Specs (`.trellis/spec/editor/`)

| File | Description |
|---|---|
| `index.md` | Editor guidelines index -- Avalonia Simple theme, MVVM, EditorState singleton |
| `directory-structure.md` | Avalonia UI layout: Views/, ViewModels/, Services/, Styles/ |
| `component-guidelines.md` | Panel/Control patterns, ViewModel base classes, XAML conventions, Common Mistakes (Avalonia Classes binding, DataTemplate ContextMenu), ViewModel-to-backend sync pattern with _syncing guard, asset panel data source pattern |
| `hook-guidelines.md` | State patterns: singleton services, command pattern, event patterns |
| `state-management.md` | Layered state: EditorState (global) > PanelState > ServiceState; HasSelectedTool common mistake |
| `native-viewport-hosting.md` | SDL/Stride viewport embedding in Avalonia |
| `quality-guidelines.md` | Editor code standards |
| `type-safety.md` | Nullable, enums, struct types, large array index overflow warning |

---

### Biome/Climate-Related Architecture and Code

This is the core area relevant to the `04-30-biome-rulelayer` task.

#### Architecture Documents (easysdd/)

| File | Description |
|---|---|
| `easysdd/architecture/DESIGN.md` | Master architecture index: 10 subsystems, 6 key decisions. Data flow overview for both editor and runtime. |
| `easysdd/architecture/climate-material.md` | ClimateMask -> GPU Compute -> MaterialIndexMap pipeline. Coordinate conversion table, data/state inventory, code anchors. |
| `easysdd/architecture/editor-services.md` | TerrainManager orchestrator, HeightEditor/PaintEditor/ClimateEditor, MarkDataDirty unified GPU sync. |
| `easysdd/architecture/shader-pipeline.md` | Runtime and editor shader pipeline: Compute build, displacement, diffuse. EditorTerrainBuildSplatMap is the core GPU Compute. |
| `easysdd/compound/2026-04-20-decision-climate-mask-r8.md` | Decision: ClimateMask R8 1/4 heightmap resolution for indirect mapping |
| `easysdd/compound/2026-04-20-decision-index-map-over-splatmap.md` | Decision: IndexMap (RGBA8) replacing traditional SplatMap for 256 materials + 3D projection |
| `easysdd/compound/2026-04-20-decision-splatmap-half-resolution.md` | Decision: SplatMap fixed at 1/2 heightmap resolution (16MB -> 4MB) |
| `easysdd/compound/2026-04-21-explore-gradient-texturing-vs-procedural-terrain-painter.md` | **Critical exploration**: Current system picks single "winner" material per texel instead of per-layer continuous masks. Identifies root cause of "mechanical" texturing: data is discretized too early. Recommends upgrading to per-material mask stack. |

#### Key Code Files (Biome/Climate)

| File | Description |
|---|---|
| `Terrain.Editor/Services/ClimateRuleService.cs` | Central singleton managing Biome (ClimateDefinition) and Layer (ClimateRuleLayer) hierarchy with BiomeModifier stack. 6 modifier types: HeightRange, SlopeRange, CurvatureRange, DirectionRange, Noise, TextureMask. Legacy compatibility properties (MinAltitude/MaxAltitude/MinSlopeDegrees/MaxSlopeDegrees/BlendRange) wrap BiomeModifier instances. |
| `Terrain.Editor/Services/ClimateMask.cs` | R8 byte array storing climate ID per pixel (1/2 heightmap resolution after half-res fix). GetValue/SetValue pixel access. |
| `Terrain.Editor/Services/ClimateEditor.cs` | Singleton applying climate ID strokes to ClimateMask. Converts world coordinates to 1/2 mask space, uses BrushParameters for radius/falloff. Probability-based soft edge blending. |
| `Terrain.Editor/Services/EditorState.cs` | Global editor state singleton. Tracks CurrentClimateId, SelectedRuleIndex, SelectedModifierIndex, EditLayerMode, HeatmapEnabled, SceneDebugViewMode (including ClimateMaskMap, LayerHeatmap, DetailIndexMap, DetailWeightMap). |
| `Terrain.Editor/ViewModels/ClimateViewModel.cs` | Avalonia ViewModel wrapping ClimateRuleService. ObservableCollection of ClimateDefinitionViewModel and RuleViewModel. CRUD commands for climates/rules. Sync from service with incremental collection update pattern. |
| `Terrain.Editor/ViewModels/ClimateDefinitionViewModel.cs` | ViewModel wrapping ClimateDefinition. Name and DebugColorBrush with _syncing guard. |
| `Terrain.Editor/ViewModels/RuleViewModel.cs` | ViewModel wrapping ClimateRuleLayer. MinAltitude/MaxAltitude/MinSlopeDegrees/MaxSlopeDegrees/BlendRange/MaterialSlotIndex with _syncing guard and immediate commit to ClimateRuleService. |
| `Terrain.Editor/Services/TomlProjectConfig.cs` | TOML config model. Supports both legacy `climate_rules` and new `biome_layers` + `biome_modifiers` sections. Full BiomeModifier config with all parameters (type, blend_mode, opacity, min/max, falloff, radius, angle, scale, offset, seed, octaves, invert, texture_mask). |
| `Terrain.Editor/Services/MaterialIndexMap.cs` | CPU-side detail control map. 4 indices + 4 weights per texel (DetailControlPixel). Legacy SetIndex/GetIndex methods. GPU upload via index/weight data arrays. |
| `Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl` | **GPU Compute shader** that generates MaterialIndexMap from ClimateMask + Biome/Layer/Modifier structured buffers. Core logic: per-texel loads biome ID, iterates matching layers, evaluates each modifier (HeightRange/SlopeRange/CurvatureRange/DirectionRange/Noise/TextureMask), blends via ApplyBlendMode, keeps top-4 weights via PushTop4. Outputs to OutputIndexMap (4 best indices) + OutputWeightMap (4 normalized weights). |
| `Terrain.Editor/Rendering/EditorTerrainSplatMapComputeDispatcher.cs` | C# dispatch for the compute shader. Sets ClimateMaskTexture, BiomeBuffer, LayerBuffer, ModifierBuffer parameters. Per-slice dispatch with 8x8 thread groups. |
| `Terrain.Editor/Rendering/EditorTerrainRenderFeature.cs:885` | EditorTerrainSplatMapComputeDispatcher integration point |

#### Data Model Hierarchy (Current State)

```
ClimateRuleService (singleton)
  |-- ClimateDefinition[] = "Biomes" (Id, Name, DebugColor)
  |-- ClimateRuleLayer[] = "Layers" (Id, Name, ClimateId, MaterialSlotIndex, PriorityOrder, Enabled, Visible)
  |     |-- BiomeModifier[] (Id, Type, BlendMode, Enabled, Visible, Opacity, Min, Max, MinFalloff, MaxFalloff, ...)
  |     |-- Legacy compatibility: MinAltitude/MaxAltitude/MinSlopeDegrees/MaxSlopeDegrees/BlendRange -> wrap BiomeModifiers
```

**Naming note**: The codebase uses "Climate" naming historically but has been incrementally aliased to "Biome" terminology. `ClimateRuleService.Climates` is also exposed as `.Biomes`, `ClimateRuleLayer` corresponds to "Layer", and `BiomeModifier` is the new modifier stack type.

#### Coordinate System (Critical for Implementation)

| From | To | Factor |
|---|---|---|
| Heightmap | ClimateMask | x4 (ClimateMask is 1/4 heightmap) |
| Heightmap | MaterialIndexMap (SplatMap) | x2 (SplatMap is 1/2 heightmap) |
| ClimateMask | MaterialIndexMap | x2 |

#### GPU Compute Shader Data Flow

```
ClimateMask (R8 texture)  -- LoadClimateId() --> biomeId per texel
Biomes StructuredBuffer   -- BiomeGpu structs (BiomeId, LayerStartIndex, LayerCount, DebugColor)
Layers StructuredBuffer   -- LayerGpu structs (LayerId, BiomeId, MaterialSlotIndex, Enabled, Visible, PriorityOrder, ModifierStartIndex, ModifierCount)
Modifiers StructuredBuffer -- ModifierGpu structs (ModifierType, BlendMode, Enabled, Opacity, Min, Max, MinFalloff, MaxFalloff, Radius, AngleDegrees, AngleRangeDegrees, Scale, OffsetX, OffsetY, Seed, Octaves, Invert)

Per texel:
1. Load biome ID from ClimateMask
2. Iterate all layers matching biomeId
3. Per layer: start weight=1.0, iterate modifiers
   - EvaluateModifier() -> modifierValue
   - ApplyBlendMode(weight, modifierValue, blendMode) -> blended
   - lerp(weight, blended, opacity) -> weight
4. PushTop4(bestIndices, bestWeights, materialSlotIndex, weight)
5. Normalize top-4 weights
6. Output to OutputIndexMap + OutputWeightMap
```

---

### Recent Journal Entries (Terrain Texturing Work)

From `.trellis/workspace/Redwarx008/journal-1.md`:

| Session | Date | Summary |
|---|---|---|
| 4 | 2026-04-26 | Climate panels + Paint/Sculpt brush integration: ClimateViewModel/ClimateDefinitionViewModel/RuleViewModel for biome/layer CRUD |
| 11 | 2026-04-30 | Brush projection decal restore: BrushDecal component/processor/render feature/shader |
| 13 | 2026-04-30 | ClimateMask half-res + SplatMap half-res pipeline: unified to 1/2 heightmap resolution, fixed GPU texture overflow on large terrains |

---

### Existing Task Research

The `04-30-biome-rulelayer` task already has one research file:

| File | Description |
|---|---|
| `.trellis/tasks/04-30-biome-rulelayer/research/unity-rulelayer-reference.md` | Detailed analysis of Unity ProceduralTerrainPainter: LayerSettings = RuleLayer, Modifier stack with 6 types (Height/Slope/Curvature/Noise/Direction/TextureMask), GPU-based mask generation with blend modes, reverse-order processing. Key finding: no Biome concept exists in Unity reference -- would need to be designed for Stride. |

---

### Key Architecture Decisions

1. **IndexMap over SplatMap** -- Supports 256 materials + 3D projection + rotation
2. **ClimateMask R8 1/4 resolution indirect mapping** -- Rule-driven > direct painting, 1/4 saves memory
3. **SplatMap fixed 1/2 heightmap resolution** -- 16MB -> 4MB memory savings
4. **Chunk transaction Undo/Redo** -- Stroke-level incremental snapshots
5. **TOML project persistence** -- Human-readable, version-controllable
6. **Independent material_descriptor.toml export** -- Runtime decoupled from editor project files

---

### Known Issues / Active Exploration

The `2026-04-21-explore-gradient-texturing-vs-procedural-terrain-painter.md` document identifies that the current system has a fundamental limitation: it picks a single "winner" material per texel instead of generating continuous per-layer masks. The GPU compute shader has already been upgraded to output top-4 indices + weights (PushTop4), but the exploration document notes the data was "discretized too early" in the original design. The current compute shader now properly supports multi-material blending, but the authoring model (ClimateRuleLayer with BiomeModifier stack) needs to be fully exposed in the editor UI.

---

### Related Archived Tasks

- `04-30-fix-material-index-map-overflow` -- Half-resolution SplatMap + ClimateMask pipeline
- `04-29-fix-brush-projection` -- BrushDecal render feature
- `04-26-migrate-imgui-features-to-avalonia` -- Climate panels migration
- `04-26-avalonia-migration-wiring` -- Climate brush wiring

---

## Caveats / Not Found

- No `.trellis/.current-task` file exists; task identity inferred from git status (`?? .trellis/tasks/04-30-biome-rulelayer/`)
- No Terrain.Editor.csproj was found in the project root (may be embedded or auto-generated by Stride)
- The exploration document (`2026-04-21-explore-gradient-texturing-vs-procedural-terrain-painter.md`) predates the compute shader upgrade that added PushTop4 and OutputWeightMap -- the compute shader has since been improved to support multi-material output
- The `ClimateRuleService.NormalizeClimateRanges` method (mentioned in the exploration document as problematic) appears to still exist but the legacy altitude/slope properties now wrap BiomeModifier instances, which may partially address the coupling concern
- No runtime biome/climate processing exists -- ClimateMask and ClimateRuleService are editor-only; runtime uses pre-computed MaterialIndexMap from .terrain export
