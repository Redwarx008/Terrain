# Research: Stride Engine Decal Rendering Support

- **Query**: Does Stride engine support decal rendering / deferred decals?
- **Scope**: internal (Stride engine source + Terrain project)
- **Date**: 2026-04-29

## Executive Summary

**Stride engine has NO built-in decal support.** There is no DecalComponent, no decal renderer, no decal shader, no decal material, and no deferred decal pipeline. The word "decal" appears only in documentation comments and an Assimp texture-map import mapping. A decal system would need to be built from scratch.

---

## Findings

### 1. Filename Search: "*decal*" and "*Decal*"

Search scope: Entire Stride engine source (`E:\WorkSpace\stride`) and Terrain project.

**Result: Zero matches in both repos.** No file anywhere in the engine or project contains "decal" or "Decal" in its filename.

### 2. Content Search: "decal" (case-insensitive)

#### Stride Engine Source (`E:\WorkSpace\stride\sources\engine`)

Only 2 files matched, both are purely documentation references:

| File | Context | Nature |
|---|---|---|
| `E:\WorkSpace\stride\sources\engine\Stride\Graphics\PixelFormat.cs` | "The BC2 format is tipically used for UI, decals, or sharp masks" | XML documentation for BC2/DXT3 pixel format |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\RasterizerStateDescription.cs` | "This value is added to the depth of each pixel and is typically used to resolve Z-fighting, such as when rendering decals or wireframe overlays on top of solid geometry." | XML documentation for DepthBias property |

These are standard API docs describing generic use cases, not any actual decal implementation.

#### Stride Tools Source

Only 3 files matched in `E:\WorkSpace\stride\sources\tools\`:

| File | Context | Nature |
|---|---|---|
| `Stride.Importer.3D\MeshConverter.cs:1478` | `Material.MappingMode.Decal => TextureAddressMode.Border` | Assimp material texture mode mapping |
| `Stride.Importer.3D\Material\Materials.cs:79` | `MappingMode.Decal // aiTextureMapMode_Decal` | Assimp import: maps Assimp's Decal texture wrapping to Stride's `Border` address mode |
| `Stride.Importer.3D\Material\MappingMode.cs:13` | `Decal,` (enum member) | Enum definition: `Wrap, Clamp, Decal, Mirror` for Assimp texture modes |

This is the **Assimp 3D model importer** translating `aiTextureMapMode_Decal` to `TextureAddressMode.Border`. This is about texture coordinate addressing (how UVs outside [0,1] are handled), NOT about rendering decals on top of geometry.

#### Legacy Test Files

`E:\WorkSpace\stride\sources\data\tests\Factory\asp3.fx` and `asp3.cgfx` contain `// DecalRGBA_Ma`, `// ModulateDecalRGBA_Ma` etc. as **commented-out legacy CgFX render state references**. These are test asset files from a CgFX shader factory test, not functional code.

### 3. Exact Match: "Decal" as Class/Identifier

Search: `E:\WorkSpace\stride\sources` for "Decal" as a standalone identifier.

**Result: No classes, structs, or namespaces named "Decal" exist.** The only hits were the same Assimp `MappingMode.Decal` enum value already described above.

### 4. Deferred Rendering Pipeline / GBuffer

Stride has a **minimal GBuffer system** that is NOT a full deferred renderer. Details:

#### GBuffer Shader
- `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Deferred\GBuffer.sdsl`
- Trivially writes `float4(streams.normalWS, 1.0f)` to `streams.ColorTarget`
- Generated C# file is empty (`// Nothing to generate`)
- The GBuffer is a **child shader**, not a g-buffer filling pass

#### GBuffer Output Shaders (extended render targets)

| File | Purpose |
|---|---|
| `GBufferOutputNormals.sdsl` | Encodes world-space normals to [0,1] range |
| `GBufferOutputSpecularColorRoughness.sdsl` | Outputs RGB specular + A roughness |
| `GBufferOutputSubsurfaceScatteringMaterialIndex.sdsl` | Outputs SSS material index |

#### Render Target Semantics (`IRenderTargetSemantic.cs`)

| Semantic Class | Shader | GBuffer Channel |
|---|---|---|
| `ColorTargetSemantic` | (none, writes to color) | Color |
| `NormalTargetSemantic` | `GBufferOutputNormals` | Normal |
| `SpecularColorRoughnessTargetSemantic` | `GBufferOutputSpecularColorRoughness` | Specular + Roughness |
| `VelocityTargetSemantic` | `VelocityOutput` | Motion vectors |
| `MaterialIndexTargetSemantic` | `GBufferOutputSubsurfaceScatteringMaterialIndex` | SSS material ID |
| `OctahedronNormalSpecularColorTargetSemantic` | `GBufferOutputNormalSpec` | Octahedron-encoded normal |
| `EnvironmentLightRoughnessTargetSemantic` | `GBufferOutputIblRoughness` | IBL roughness |

These semantics are used by post-processing effects (SSR in `LocalReflections`, AO in `AmbientOcclusion`, TAA in `TemporalAntiAliasEffect`) to read GBuffer data. **None of these are used for decal rendering.**

#### Compositing Profiling Keys (`CompositingProfilingKeys.cs`)

The compositor has profiling keys for `Opaque`, `Transparent`, `MsaaResolve`, `LightShafts`, and `GBuffer`. **No decal pass or deferred decal pass exists.**

### 5. Render Features (no decal feature exists)

All RenderFeature/SubRenderFeature classes in `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\`:

| Class | Role |
|---|---|
| `MeshRenderFeature` | Renders meshes (main feature) |
| `TransformRenderFeature` | Computes world transforms |
| `MaterialRenderFeature` | Material parameter setup |
| `SkinningRenderFeature` | Skeletal animation |
| `InstancingRenderFeature` | GPU instancing |
| `SpriteRenderFeature` | Sprite rendering |
| `BackgroundRenderFeature` | Skybox/background |
| `ShadowCasterRenderFeature` | Shadow map rendering |
| `ForwardLightingRenderFeature` | Forward lighting pass |
| `SubsurfaceScatteringRenderFeature` | SSS post-pass |
| `MeshVelocityRenderFeature` | Motion vectors |

**No DecalRenderFeature, ProjectorRenderFeature, or any similar feature exists.**

### 6. Projection / Projector Search

Search for "project" (case-insensitive) in the rendering directory returned only standard usages:
- `ProjectionMatrix` calculations (standard 3D math)
- `ViewProjection` matrix usages
- `LightSpot.cs` / `LightPoint.cs` for spot/point light frustum projection
- Mesh velocity's `PreviousWorldViewProjection`

**No "projector" component or projection-based decal rendering exists.**

### 7. Entity Components (no DecalComponent)

All component classes in `E:\WorkSpace\stride\sources\engine\Stride.Engine\Engine\`:

| Component | Purpose |
|---|---|
| `TransformComponent` | Position/Rotation/Scale |
| `ModelComponent` | Mesh rendering |
| `CameraComponent` | Camera/view |
| `LightComponent` | Dynamic lighting |
| `SpriteComponent` | 2D sprites |
| `BackgroundComponent` | Skybox/background |
| `AnimationComponent` | Animation playback |
| `AudioEmitterComponent` | Audio source |
| `AudioListenerComponent` | Audio listener |
| `ScriptComponent` | C# scripts |
| `LightShaftComponent` | Volumetric light shafts |
| `LightProbeComponent` | Light probes |
| `InstancingComponent` | GPU instancing config |
| `InstanceComponent` | Instance overrides |

**No DecalComponent exists.**

### 8. Terrain Project Search

Search scope: `e:\Stride Projects\Terrain` for "decal" (case-insensitive).

**Result: Zero matches.** The Terrain project has no decal-related code of any kind.

The project's existing research file `stride-overlay-rendering.md` documents brush cursor overlay rendering approaches but does not mention decals.

---

## Detailed Analysis: What Stride DOES Have

### Forward Rendering Pipeline

Stride uses a **forward rendering** pipeline by default. The `StrideForwardShadingEffect.sdfx` composes:
1. `StrideEffectBase` (base effect utilities)
2. `MaterialSurfacePixelStageCompositor` (material rendering)
3. `child GBuffer` (writes normals to color target for GBuffer pass)
4. `StrideLighting` (direct + environment light groups)
5. Shadow map casters

The GBuffer is used as a **supplementary output** within the forward pass, NOT as a separate deferred geometry pass. Materials render directly to the color target AND optionally to extended GBuffer render targets simultaneously.

### Post-Processing Effects

The `PostProcessingEffects.cs` pipeline includes, in order:
1. Outline
2. FXAA (optionally first, Karis hybrid method)
3. Ambient Occlusion (reads depth)
4. Screen-Space Local Reflections (reads depth + normals + specular/roughness from GBuffer)
5. Depth of Field
6. Fog
7. Luminance / Tone Mapping
8. Color Transforms (grading)
9. Bloom / Light Streaks / Lens Flare
10. FXAA (optionally last)

**No decal pass is injected into this pipeline. There is no "before post-processing" decal injection point built in.**

---

## What Would Be Required for Decal Support

To add decal rendering to Stride, one would need to build:

1. **Asset type**: A decal texture/material asset
2. **Component**: `DecalComponent` (EntityComponent with transform, texture, size, blending params)
3. **Processor**: Extracts decal components, creates RenderDecal objects
4. **RenderFeature**: `DecalRenderFeature` (RootRenderFeature or SubRenderFeature)
   - Projects a box/sphere volume into screen space
   - Reads GBuffer (depth, normals) to reconstruct world position
   - Samples decal texture in projected UV space
   - Blends decal color into the GBuffer albedo or a separate decal accumulation buffer
5. **Shader(s)**: SDSL shaders for screen-space decal projection
6. **Compositor integration**: Decal pass injected between opaque rendering and post-processing

### GBuffer Data Available for Decals

The existing GBuffer provides sufficient data for screen-space decals:

| Data | Source | Format |
|---|---|---|
| Depth | DepthStencil buffer | D32_Float / D24_UNorm_S8_UInt |
| World Normals | `NormalTargetSemantic` (encoded) | RGBA8 / RGBA16 |
| Specular + Roughness | `SpecularColorRoughnessTargetSemantic` | RGBA8 |

For albedo modification via decals, an additional `AlbedoTargetSemantic` GBuffer output would need to be added (Stride does not currently output albedo to a separate render target -- it renders directly to the color buffer).

### Alternative: Forward Decals

Instead of deferred screen-space decals, one could implement forward decals:
- Render a decal bounding volume (box/sphere) as mesh geometry
- Use stencil or depth-fail technique to project onto underlying surfaces
- Blend decal texture using alpha blending with depth write disabled

This is simpler but less efficient than deferred decals for many decals.

---

## Caveats / Not Found

- **No third-party decal plugins** were found in the Stride ecosystem search.
- **No decal-related forum discussions or community extensions** were found in the engine source.
- **No "RenderTargetExtensions" for albedo** -- the Stride forward pipeline does not separate albedo into its own render target, which would be needed for deferred decal albedo modification.
- **Depth-only prepass**: Stride does not have a depth prepass by default; the GBuffer pass is combined with the main material pass. For screen-space decals to work efficiently, a depth+normals prepass would be beneficial.
- **MSAA support**: Deferred decals are typically incompatible with MSAA; Stride's MSAA resolve step in the compositor would complicate integration.
