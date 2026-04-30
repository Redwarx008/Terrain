# Research: Unity ProceduralTerrainPainter RuleLayer System

- **Query**: Understand the RuleLayer system for procedural terrain texturing in the Unity ProceduralTerrainPainter project
- **Scope**: internal (Unity project at E:/UnityProjects/My project (1)/Assets/ProceduralTerrainPainter)
- **Date**: 2026-04-30

## Findings

### Files Found

| File Path | Description |
|---|---|
| `Runtime/LayerSettings.cs` | Defines LayerSettings - the equivalent of a "RuleLayer": one terrain layer + its modifier stack |
| `Runtime/ModifierStack.cs` | Core processing engine: configures material, iterates layers, executes GPU-based modifier passes |
| `Runtime/Modifiers/Modifier.cs` | Abstract base class for all modifiers with BlendMode, Opacity, and FilterPass enum |
| `Runtime/Modifiers/Height.cs` | Height modifier: min/max height range with falloff |
| `Runtime/Modifiers/Slope.cs` | Slope modifier: min/max slope angle (0-90 degrees) with falloff |
| `Runtime/Modifiers/Curvature.cs` | Curvature modifier: convexity/concavity detection with Soft/Hard solver |
| `Runtime/Modifiers/Noise.cs` | Noise modifier: Simplex or Gradient noise with scale/offset/level remapping |
| `Runtime/Modifiers/Direction.cs` | Direction modifier: aspect-based (which direction a slope faces) with sun direction option |
| `Runtime/Modifiers/TextureMask.cs` | Texture mask modifier: sample an external texture's channel as a mask |
| `Runtime/TerrainPainter.cs` | Main MonoBehaviour: holds terrain array, layer settings list, orchestrates repaint |
| `Runtime/Utilities.cs` | Helper: splatmap/channel index math, bounds calculation, settings-to-layers conversion |
| `Runtime/Attributes.cs` | Custom property attributes: ResolutionDropdown, MinMaxSlider, ChannelPicker |
| `Runtime/TerrainChangeListener.cs` | Auto-repaint on heightmap change |
| `Shaders/Modifier.shader` | GPU shader with 6 passes (Height, Slope, Curvature, TextureMask, Noise, Direction) |
| `Shaders/Filters.hlsl` | Filter functions: HeightMask, SlopeMask, CurvatureMask, CurvatureFromNormal, SlopeFromNormal |
| `Shaders/Noise.hlsl` | Noise functions: GradientNoise, SimplexNoise (fractal octave) |
| `Editor/TerrainPainterInspector.cs` | Full custom inspector: layer list, modifier stack UI, heatmap preview |
| `Editor/ModifierEditor.cs` | Modifier type discovery via reflection |
| `Editor/PropertyDrawers.cs` | Custom drawers for MinMaxSlider, ResolutionDropdown, ChannelPicker |
| `Editor/HeatmapPreview.cs` | Scene-view heatmap visualization of layer masks |
| `Editor/Heatmap.shader` | Shader for rendering heatmap overlays in scene view |

### Architecture Overview

This Unity project implements a **layer-based procedural terrain painting system** using GPU compute. It does NOT have a "Biome" concept or "Biome map" (R8 texture). Those concepts are something that would need to be added or designed separately. What it does have is a flat list of "Layer Settings" where each layer defines rules for where a terrain texture appears.

#### Core Data Model

```
TerrainPainter (MonoBehaviour)
  |-- terrains: Terrain[]                // Target terrain objects
  |-- layerSettings: List<LayerSettings>  // Ordered list of paint layers
  |-- splatmapResolution: int            // Splatmap resolution (64-1024)
  |
  +-- LayerSettings (one per terrain texture layer)
       |-- enabled: bool
       |-- layer: TerrainLayer            // Unity terrain layer (texture, normal, etc.)
       |-- modifierStack: List<Modifier>  // Ordered list of filter/modifier rules
       |
       +-- Modifier (abstract base)
            |-- enabled: bool
            |-- label: string
            |-- blendMode: BlendMode      // How this modifier combines with previous
            |-- opacity: float (0-100)    // Strength of this modifier
            |-- passIndex: FilterPass     // Which shader pass to use
```

#### LayerSettings = "RuleLayer" Equivalent

The `LayerSettings` class IS the "RuleLayer" in this system:

```csharp
// Runtime/LayerSettings.cs
[System.Serializable]
public class LayerSettings
{
    public bool enabled = true;
    public TerrainLayer layer;
    public List<Modifier> modifierStack = new List<Modifier>();
}
```

Each LayerSettings binds one `TerrainLayer` (the actual texture material) to a stack of `Modifier` rules that determine WHERE on the terrain this texture appears.

#### Modifier = "Rule" / "Criteria"

The `Modifier` base class (Runtime/Modifiers/Modifier.cs):

```csharp
[Serializable]
public class Modifier : ScriptableObject
{
    public bool enabled = true;
    public string label;
    public BlendMode blendMode;    // Multiply, Add, Subtract, Min, Max
    public float opacity = 100;    // 0-100, controls strength
    
    public enum FilterPass
    {
        Height,       // Pass 0
        Slope,        // Pass 1
        Curvature,    // Pass 2
        TextureMask,  // Pass 3
        Noise,        // Pass 4
        Direction     // Pass 5
    }
    public FilterPass passIndex;
}
```

Each Modifier subclass sets its `passIndex` in `OnEnable()` and overrides `Configure(Material)` to push parameters to the shader.

#### Available Modifier Types (6 total)

1. **Height** (`Height.cs`): Range [min, max] with falloff on each side. Params: `_MinMaxHeight` (float4: min, max, minFalloff, maxFalloff)

2. **Slope** (`Slope.cs`): Range [0, 90] degrees with falloff. Params: `_MinMaxSlope` (float4: min, max, minFalloff, maxFalloff). Computed from heightmap derivatives, NOT from normal map.

3. **Curvature** (`Curvature.cs`): Convexity/concavity detection. Two solvers: "Soft" (overlay blend of normal derivatives) and "Hard" (cross-product based). Params: `_MinMaxCurvature` (float4), `_CurvatureRadius`, `_CurvatureSolver`.

4. **Noise** (`Noise.cs`): Simplex or Gradient noise. Params: `_NoiseScaleOffset` (float4: scaleX, scaleY, offsetX, offsetY), `_Levels` (float4: min, max, 0, 0), `_NoiseType` (0=Simplex, 1=Gradient). Applied via `smoothstep(levels.x, levels.y, noise)`.

5. **Direction** (`Direction.cs`): Aspect/facing direction. Uses normal map dot product with a configurable direction vector. Params: `_Direction` (float3), `_DirectionLevels` (float2: min, max). Can optionally add sun direction.

6. **TextureMask** (`TextureMask.cs`): Sample an external texture as a mask. Params: `_MaskTexture`, `_Channel` (R/G/B/A), `_TilingParams` (tiling, spanTerrains flag). Supports spanning across multiple terrains.

### How Modifiers Combine (Blending Pipeline)

This is the critical part for understanding the RuleLayer architecture:

#### Per-Layer Processing (ModifierStack.ProcessSingleLayer)

```
1. Create alphaMap RenderTexture (R8_UNorm, resolution x resolution)
2. Initialize alphaMap to WHITE (1.0 everywhere) -- this is the base mask
3. Iterate modifiers in REVERSE ORDER (bottom of stack first)
   For each modifier:
   a. modifier.Configure(filterMat) -- push parameters to shader
   b. modifier.Execute(alphaMap) -- GPU Blit using the modifier's shader pass
      - The shader reads alphaMap as _MainTex (current accumulated mask)
      - The shader computes the modifier's mask value
      - Result = lerp(BASE, mask, _Opacity)
      - BASE = 0.0 for Add/Subtract/Max, 1.0 for Multiply/Min
      - Then GPU blend mode is applied (Multiply/Add/Subtract/Min/Max)
4. The final alphaMap is blitted to the terrain's splatmap channel
```

#### Blend Modes (Modifier.BlendMode)

```csharp
public enum BlendMode
{
    Multiply,    // Default. Narrows the mask. Uses DstColor*SrcColor
    Add,         // Expands the mask. Uses SrcColor+DstColor  
    Subtract,    // Removes from mask. Uses ReverseSubtract (Dst-Src)
    Min,         // Takes minimum
    Max          // Takes maximum
}
```

The blending uses GPU render state (BlendOp + Blend factors), NOT shader math. The `SetBlendMode` method in `Modifier.cs` configures `Blend[_SrcFactor][_DstFactor]` and `BlendOp[_BlendOp]` on the material.

Key insight: "Source" in the blend is the CURRENT filter output. "Destination" is the PREVIOUS accumulated result. This means:
- **Multiply** (default starting point): Each new modifier narrows where the layer appears
- **Add**: Each new modifier expands where the layer appears  
- **Subtract**: Each new modifier removes from where the layer appears

#### Layer Weight Normalization (Unity PaintContext)

After all layers are processed, Unity's `PaintContext` handles normalization. From `ModifierStack.cs:104-106`:

```csharp
//PaintContext handles creation of splatmaps. Subtracting the weights of a splatmap, from the ones before it.
//A single pixels of all combined alpha maps must not exceed a value of one. The 2nd pass of Hidden/TerrainEngine/TerrainLayerUtils is used internally to do this
```

Unity's internal `TerrainLayerUtils` shader ensures all splatmap channels sum to 1.0 per pixel. Layers are processed in **reverse order** (last layer first), and each subsequent layer's weight is subtracted from what's available.

### Shader Implementation Details

#### Modifier.shader Structure

The shader has 6 passes, one per FilterPass enum value. Each pass:
- Reads `_MainTex` (current accumulated alpha mask, R channel)
- Computes its specific mask
- Outputs `lerp(BASE, mask, _Opacity)` where BASE depends on blend mode
- GPU blending combines this output with the existing alphaMap

#### Filter Functions (Filters.hlsl)

All mask functions follow the same pattern with min/max + falloff:

```hlsl
// Generic pattern:
float minEnd = (MIN - FALLOFF);
float minWeight = saturate((minEnd - (value - MIN)) / (minEnd - MIN));
float maxEnd = MAX + FALLOFF;
float maxWeight = saturate((maxEnd - (value - MAX)) / (maxEnd - MAX));
return saturate(maxWeight * minWeight);
```

This creates a smooth band-pass filter where:
- Inside [min, max]: mask = 1.0
- Below min: fades from 0 to 1 over [min-falloff, min]
- Above max: fades from 1 to 0 over [max, max+falloff]

### What Does NOT Exist in This Project

1. **No "Biome" concept** - There is no Biome class, no biome grouping of layers, no biome map. All LayerSettings are in a single flat list.

2. **No biome map texture (R8)** - There is no texture that selects which biome applies at each pixel. All layers share the same global rules.

3. **No "RuleLayer" class** - The term "RuleLayer" is not used in the codebase. The equivalent is `LayerSettings`.

4. **No config/serialization format** - LayerSettings are serialized inline with the TerrainPainter MonoBehaviour (Unity serialization). There are no ScriptableObjects, JSON, or TOML configs for biomes/rules.

5. **No layer priority within groups** - All layers are in one list. The last layer (index 0 after reverse iteration) is the "base layer" that fills the entire terrain. Priority is purely order-based.

### Execution Flow Summary

```
TerrainPainter.RepaintAll()
  |
  v
For each terrain:
  ModifierStack.Configure(terrain, bounds, resolution)
    - Set heightmap texture, normal map, height scale, terrain position/scale on material
    - Create alphaMap RenderTexture (R8_UNorm)
  |
  v
  ModifierStack.ProcessLayers(terrain, layerSettings)
    - Iterate layers in REVERSE ORDER (bottom of list = processed first)
    |
    v For each LayerSettings:
      ProcessSingleLayer(terrain, settings)
        1. alphaMap = white (full mask)
        2. Iterate modifiers in REVERSE ORDER
           - Configure material params
           - GPU Blit: filter shader pass -> alphaMap (with blending)
        3. Blit alphaMap -> terrain splatmap via PaintContext
           (PaintContext normalizes weights across all layers)
  |
  v
  Apply stamps (extensibility point)
  Regenerate basemap
  Refresh vegetation
  Fire OnTerrainRepaint event
```

### Implications for Stride Implementation

1. **LayerSettings = BiomeRuleLayer**: Each LayerSettings is effectively a "rule layer" - a texture + rules for where it appears. In a biome system, these would be grouped per-biome.

2. **Modifier stack = Rule criteria**: The 6 modifier types (Height, Slope, Curvature, Noise, Direction, TextureMask) are the rule criteria. A biome-aware system would add a "BiomeMask" modifier or use the biome map as a top-level selector.

3. **GPU-based mask generation**: The entire pipeline is GPU-driven using render-to-texture with blending. This is efficient and avoids CPU-GPU round trips.

4. **Weight normalization**: Unity handles this via PaintContext. In Stride, this would need custom implementation.

5. **Reverse order processing**: Layers and modifiers are processed bottom-to-top. This is important for the priority/weight system.

6. **Blend modes are GPU state**: Not shader math. The actual combination of modifier masks happens through GPU blend operations, not in the shader code itself.

## Caveats / Not Found

- **No Biome/BiomeMap concept exists** in this Unity project. The user's question about "Biome map (R8 texture that selects which biome)" refers to a concept that would need to be designed and implemented new for the Stride project.
- **No external config format** for rules/layers. Everything is Unity-serialized MonoBehaviour data.
- The project uses Unity's `PaintContext` API for splatmap weight normalization, which is Unity-specific and would need a custom equivalent in Stride.
- The `TextureMask` modifier could theoretically serve as a biome mask (by feeding an R8 biome texture), but it is not used that way in the current project.
