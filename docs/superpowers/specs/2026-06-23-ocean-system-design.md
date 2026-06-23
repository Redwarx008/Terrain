# Ocean System Design

**Date:** 2026-06-23  
**Status:** Draft for review  
**Scope:** Runtime/editor ocean rendering, shared map-surface coordination, sea-level editing

## Goal

Add a full-map horizontal ocean system that works in both the editor viewport and runtime.

The ocean must:

- render as a global horizontal water plane across the whole map
- expose sea level as an editor-adjustable map setting
- persist sea level to `game/map/default.toml` under `[settings].sea_level`
- use CK3-style water textures and water-material structure as reference
- use Stride scene lighting and skybox/environment data instead of CK3-specific sun or ambient constants
- share sea level with river bottom under-ocean fade
- avoid terrain/river/ocean initialization-order coupling

The default sea level is `3.8`, matching the CK3 RenderDoc ocean draw reference where the ocean world-space Y coordinate was `3.799999952316284`.

## Non-Goals

- Do not clip the ocean plane against the heightmap in the first slice.
- Do not generate shoreline meshes.
- Do not implement coastal foam based on terrain intersection yet.
- Do not make river depend on ocean.
- Do not move map settings ownership into `OceanComponent`.
- Do not port CK3 lighting constants or province/fog/flat-map inputs.
- Do not register render features from runtime application code when scene/compositor assets should own them.

## Architecture

Introduce a shared map-surface coordinator entity:

```text
MapSurfaceRoot Entity
  MapSurfaceComponent

Terrain Entity
  TerrainComponent

River Entity
  RiverComponent

Ocean Entity
  OceanComponent
```

`MapSurfaceRoot` coordinates terrain, river, and ocean. It is not the host for their render components.

`MapSurfaceComponent` remains thin:

```csharp
public sealed class MapSurfaceComponent : EntityComponent
{
    public Entity? TerrainEntity { get; set; }
    public Entity? RiverEntity { get; set; }
    public Entity? OceanEntity { get; set; }

    internal MapSurfaceRuntimeState RuntimeState { get; } = new();
}
```

It does not expose `SeaLevel` directly. Sea level belongs to map settings, not to the coordinator.

It does not maintain generic version counters. First-slice invalidation is explicit through coordinator methods such as `SetSeaLevel`, `SetHeightScale`, and `SetRiverWidth`.

## Map Surface Runtime State

`MapSurfaceRuntimeState` stores only coordinator-private load state:

```csharp
internal sealed class MapSurfaceRuntimeState
{
    public RuntimeMapDefinition? MapDefinition { get; set; }
    public TerrainRuntimeResourceBundle? Resources { get; set; }
    public bool ResourcesLoaded { get; set; }
    public bool ContextApplied { get; set; }
}
```

The coordinator owns shared runtime loading so `TerrainProcessor` and `RiverProcessor` do not independently bootstrap the same map resources as their primary path.

For compatibility during migration, existing processor fallback behavior may remain temporarily, but the intended path is:

```text
MapSurfaceProcessor loads map resources once
Terrain consumes shared terrain resource input
Ocean consumes shared water resource input
River consumes shared river resource input and terrain height source
```

## Coordinator Flow

`MapSurfaceProcessor` drives state in a deterministic order:

```text
Resolve Terrain/River/Ocean components
Load RuntimeMapDefinition and shared runtime resources
Apply height_scale to TerrainComponent
Wait for TerrainComponent initialization
Publish terrain height source and map dimensions
Apply sea_level and water resources to OceanComponent
Apply sea_level, river settings, and height source to RiverComponent
```

The processor only writes component state and runtime input. It does not draw.

Render processors build render objects from their own component state:

- `TerrainProcessor` initializes terrain and exposes height sampling data.
- `OceanProcessor` creates or updates ocean render objects.
- `RiverProcessor` generates river meshes only when a height source is explicitly available.

Render features only draw render objects and bind GPU state. They do not perform map-resource loading or cross-system discovery.

## Sea-Level Ownership

Sea level is part of runtime map settings:

```toml
[settings]
height_scale = 200
river_min_width = 1
river_max_width = 4
river_max_visible_camera_height = 2428
sea_level = 3.8
```

Reader behavior:

- `RuntimeMapDefinitionReader` accepts `sea_level` as a valid `[settings]` key.
- Missing `sea_level` defaults to `3.8`.
- Unknown setting validation remains strict.

Writer behavior:

- `MapDefinitionWriter` writes `sea_level`.
- Saving an old map fills the setting with the current value.

Runtime ownership:

- `RuntimeMapDefinition` exposes sea level as map setting data.
- `MapSurfaceProcessor` reads it and distributes it.
- `OceanComponent` and `RiverComponent` may cache the current runtime input but do not own the setting.

## Editor Flow

The editor settings view adds a `Sea Level` control.

Data flow:

```text
SettingsViewModel.SeaLevel
  -> EditorShellViewModel.OnSettingsPropertyChanged
  -> MapSurface coordinator API
  -> RuntimeMapDefinition settings update
  -> Ocean runtime input update
  -> River bottom ocean-fade height update
  -> map definition marked dirty
```

`Sea Level` belongs beside global map settings, not inside an ocean-only panel, because the value affects both ocean rendering and river bottom fade.

Immediate editor behavior:

- Changing sea level moves the ocean plane.
- Changing sea level updates river bottom `_WaterHeight`.
- Changing sea level does not rebuild terrain.
- Changing sea level does not rebuild river meshes.

Visibility controls are separate:

- `Show Terrain`
- `Show Rivers`
- `Show Ocean`

`Show Ocean` controls ocean render visibility only. It does not affect the stored sea level.

## Explicit Change Propagation

The first slice uses explicit coordinator methods instead of generic version counters:

```text
SetSeaLevel(value)
  -> update RuntimeMapDefinition settings
  -> ocean.SetSeaLevel(value)
  -> river.SetOceanFadeHeight(value)
  -> mark map definition dirty

SetHeightScale(value)
  -> update RuntimeMapDefinition settings
  -> terrain.SetHeightScale(value)
  -> river.InvalidateMeshes()
  -> mark map definition dirty

SetRiverWidth(min, max)
  -> update RuntimeMapDefinition settings
  -> river.InvalidateMeshes()
  -> mark map definition dirty

SetRiverMaxVisibleCameraHeight(value)
  -> update RuntimeMapDefinition settings
  -> river.SetMaxVisibleCameraHeight(value)
  -> mark map definition dirty
```

Generic versioning can be introduced later only if asynchronous rebuilds, hot reload, undo batching, or stale background tasks require it.

## Ocean Component

`OceanComponent` is the scene hook and runtime input container for ocean rendering:

```csharp
public sealed class OceanComponent : EntityComponent
{
    public bool Visible { get; set; } = true;
    public OceanMaterialSettings Material { get; set; } = OceanMaterialSettings.Default;

    internal OceanRuntimeInput? RuntimeInput { get; private set; }

    internal void ApplyRuntimeInput(OceanRuntimeInput input)
    {
        RuntimeInput = input;
    }
}
```

`OceanRuntimeInput` is supplied by `MapSurfaceProcessor`:

```csharp
internal readonly record struct OceanRuntimeInput(
    float SeaLevel,
    int MapWidth,
    int MapHeight,
    OceanResourceSet Resources);
```

The runtime input is not the authority for persistence. It is the current value used by the render path.

## Ocean Processor and Render Object

`OceanProcessor` owns only ocean render-object lifecycle:

- If `OceanComponent` has no runtime input, do not create a render object.
- When runtime input is available, create a full-map horizontal plane.
- When sea level changes, update plane height and shader parameters.
- When resources change, update texture bindings.
- When visibility changes, update render-object visibility.

`OceanRenderObject` stores GPU-facing data:

```csharp
internal sealed class OceanRenderObject : RenderObject
{
    public OceanComponent Source { get; }
    public float SeaLevel { get; set; }
    public Vector2 MapSize { get; set; }

    public Texture WaterColorTexture { get; set; }
    public Texture AmbientNormalTexture { get; set; }
    public Texture FlowMapTexture { get; set; }
    public Texture FlowNormalTexture { get; set; }
    public Texture FoamTexture { get; set; }
    public Texture FoamRampTexture { get; set; }
    public Texture FoamMapTexture { get; set; }
    public Texture FoamNoiseTexture { get; set; }
}
```

The first ocean plane covers:

```text
(0, seaLevel, 0)
(mapWidth, seaLevel, 0)
(0, seaLevel, mapHeight)
(mapWidth, seaLevel, mapHeight)
```

UVs follow CK3's map-normalized convention:

```text
uv.x = world.x / mapWidth
uv.y = 1 - world.z / mapHeight
```

## River Integration

`RiverProcessor` should stop relying on `VisibilityGroup.RenderObjects` scanning as its primary terrain discovery mechanism.

The coordinator supplies the terrain height source explicitly after terrain is initialized:

```text
TerrainComponent initialized
  -> MapSurfaceProcessor gets height source and map dimensions
  -> RiverComponent receives height source and river settings
  -> RiverProcessor may generate meshes
```

River bottom under-ocean fade uses the same sea level as ocean:

```text
sea_level
  -> Ocean plane Y
  -> RiverBottom _WaterHeight
```

The existing hard-coded river bottom `_WaterHeight = 3.0f` binding is replaced by the map setting.

## Water Resources

Water resources live under:

```text
game/map/water/
```

Existing local resources are preserved. The first implementation should add only missing resources needed for the ocean path.

Required ocean resource set:

```csharp
internal sealed class OceanResourceSet
{
    public required Texture WaterColor { get; init; }
    public required Texture AmbientNormal { get; init; }
    public required Texture FlowMap { get; init; }
    public required Texture FlowNormal { get; init; }
    public required Texture Foam { get; init; }
    public required Texture FoamRamp { get; init; }
    public required Texture FoamMap { get; init; }
    public required Texture FoamNoise { get; init; }
}
```

Local expected files:

```text
water_color.dds
ambient_normal.dds
flowmap.dds
flow_normal.dds
foam.dds
foam_ramp.dds
foam_map.dds
foam_noise.dds
```

`flowmap.dds` is missing locally and should be copied from:

```text
E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\flowmap.dds
```

If a required texture is missing or cannot be loaded, ocean rendering should disable itself with a clear log message that includes the path. Terrain rendering should continue.

## Shader Design

Add an ocean SDSL shader path following the Stride shader asset workflow.

The shader should borrow CK3's water-material structure:

- water color texture
- ambient normal texture
- flowmap
- flow normal texture
- foam texture
- foam ramp
- foam map
- foam noise
- refraction support where it fits the existing Stride render path

The shader must not depend on CK3-only inputs:

- `ProvinceColorTexture`
- `BorderDistanceField`
- `PatternTexture`
- `FogOfWarAlpha`
- `FlatMapTexture`
- CK3 sun direction constants
- CK3 ambient constants

Suggested shader parameters:

```c
float _SeaLevel;
float2 _MapSize;
float _Time;
float _NormalStrength;
float _FoamIntensity;
float _ReflectionIntensity;
float _WaterDiffuseMultiplier;

Texture2D WaterColorTexture;
Texture2D AmbientNormalTexture;
Texture2D FlowMapTexture;
Texture2D FlowNormalTexture;
Texture2D FoamTexture;
Texture2D FoamRampTexture;
Texture2D FoamMapTexture;
Texture2D FoamNoiseTexture;
```

Lighting requirement:

- direct light comes from Stride scene light data
- environment reflection comes from Stride skybox/environment data
- water normals come from CK3-style ambient/flow normal composition
- no shader-side hard-coded sun or ambient values in the configured path

If Stride lighting integration is too risky for the first compileable slice, the first implementation may land with plane, textures, normal/foam sampling, and a minimal Stride-compatible light response, then refine reflection and full scene-light parity in a follow-up. The architecture must still keep the final lighting path in mind and avoid one-off temporary ownership.

## Scene and Asset Integration

Runtime scene assets should contain independent entities:

- `MapSurfaceRoot` with `MapSurfaceComponent`
- `Terrain` with `TerrainComponent`
- `River` with `RiverComponent`
- `Ocean` with `OceanComponent`

`MapSurfaceComponent` references the three subsystem entities explicitly. A child-entity lookup fallback may exist for migration, but explicit references are the expected scene-authored path.

Graphics compositor requirements:

- register the ocean render feature
- assign the intended render stage and render group
- keep terrain, river, and ocean draw responsibilities separate

SDSL requirements:

- include ocean shader folders in `.sdpkg`
- include generated `*.sdsl.cs` key files in `.csproj`
- refresh generated shader keys after adding shader parameters
- run Stride clean/compile asset targets before runtime visual verification

## Error Handling

Coordinator errors:

- Missing terrain, river, or ocean entity reference logs a clear warning and waits.
- Missing map definition logs an error and leaves existing terrain behavior intact where possible.
- Terrain not initialized is not an error; coordinator waits and retries next frame.

Ocean errors:

- Missing required texture disables ocean rendering and logs the missing path.
- Shader or render-object creation failure disables ocean rendering and logs the exception.
- Visibility changes should not reload resources.

River errors:

- Missing terrain height source waits until coordinator supplies one.
- Missing river resource keeps the existing no-river behavior.
- Sea-level changes update river bottom fade without rebuilding river meshes.

## Testing and Verification

Use deterministic automated tests for data/config behavior:

- `RuntimeMapDefinitionReader` accepts `sea_level`.
- Missing `sea_level` defaults to `3.8`.
- Unknown setting validation remains active.
- `MapDefinitionWriter` writes `sea_level`.
- Existing height scale and river settings still round-trip.
- Ocean resource loader reports missing required textures with clear paths.
- `flowmap.dds` is resolved from `game/map/water`.

Use shader and visual verification for rendering behavior:

- Ocean SDSL file is registered in `.sdpkg`.
- Generated shader key file is refreshed and compiled.
- Stride asset clean/compile passes.
- Editor viewport starts without ocean-related exceptions.
- `MapSurfaceRoot`, `Terrain`, `River`, and `Ocean` entities are present.
- `OceanRenderObject` is created after terrain dimensions are known.
- Dragging sea level changes ocean height immediately.
- Dragging sea level updates river bottom `_WaterHeight`.
- Saving writes `sea_level` to `game/map/default.toml`.
- A viewport screenshot shows a full-map horizontal water surface.
- Optional RenderDoc capture confirms ocean draw call, bound water textures, and `_SeaLevel`.

Recommended verification commands after implementation:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
dotnet msbuild Terrain/Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain/Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain/Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug
dotnet build Terrain.sln --no-restore
git diff --check
```

## Open Risks

- Ocean depth state and render ordering may need tuning to avoid hiding terrain incorrectly.
- Some CK3 DDS resources may need conversion if Stride cannot load their format.
- Full Stride lighting parity for custom ocean SDSL may require iterative RenderDoc inspection.
- Editor and runtime scene assets can drift if one path creates entities dynamically; the preferred path is scene-authored entities plus editor-only safety fallback.
- Keeping old river terrain discovery fallback during migration may hide coordinator wiring mistakes; it should be removed once the coordinator path is stable.

## Implementation Order

1. Add map setting support for `sea_level`.
2. Add or update tests for reader/writer behavior.
3. Add `MapSurfaceComponent` and `MapSurfaceProcessor`.
4. Wire scene/editor entities so MapSurface references Terrain, River, and Ocean.
5. Refactor runtime resource loading toward the coordinator path.
6. Add `OceanComponent`, `OceanProcessor`, and `OceanRenderObject`.
7. Add ocean resource loading and copy missing `flowmap.dds`.
8. Add ocean SDSL shader and generated shader keys.
9. Add ocean render feature and compositor registration.
10. Replace river bottom hard-coded ocean height with map sea level.
11. Add editor sea-level control and save integration.
12. Run tests, Stride shader asset workflow, runtime/editor smoke tests, and visual verification.

