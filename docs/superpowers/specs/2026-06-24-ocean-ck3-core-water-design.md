# Ocean CK3 Core Water Design

**Date:** 2026-06-24
**Status:** Draft for review
**Scope:** Make the existing Ocean draw visually approach CK3 core ocean water without porting strategy-layer overlays

## Goal

Replace the current simplified Ocean shader with a CK3-style core water path.

The target reference is CK3 `C:\Users\Redwa\Desktop\save.rdc` event `1061`. The current project reference is `C:\Users\Redwa\Desktop\debug.rdc` event `252`.

The goal is not exact full CK3 map rendering. The goal is to make the ocean water itself behave like CK3 water:

- dark, deep water energy range instead of overbright cyan HDR output
- shoreline-aware water fade and foam
- refraction against the current scene color/depth
- see-through tinting based on underwater distance
- CK3-style flow normal, ambient wave normal, foam, fresnel, and reflection composition
- use the existing Stride scene sun/skybox binding rather than CK3 fixed sun constants

## RenderDoc Findings

Project Ocean event `252`:

- D3D11 draw indexed, 6 indices, full-map Ocean quad.
- Pixel shader binds water color, ambient normal, flowmap, flow normal, foam textures, environment map, and scene shadow.
- Shader is a simple texture/color/foam/lighting model.
- Representative Ocean pixel output was HDR-bright, around `[3.23, 4.48, 4.61, 0.86]`.
- The pass writes main `R16G16B16A16_FLOAT` scene RT with ordinary alpha blend.
- It does not use refraction, depth payload, terrain height lookup, see-through attenuation, or CK3 water fade.

CK3 Ocean event `1061`:

- D3D11 draw, 4 vertices, full-map sea-level quad.
- Vertex shader only builds world position from `POSITION.xy`, `_WaterHeight`, `MapSize`, and `ViewProjectionMatrix`.
- Pixel shader binds 18 textures and 9 constant buffers.
- Core water path includes height lookup, water color map, flowmap, flow normal, ambient normal, foam, foam ramp/map/noise, refraction texture, and reflection cubemap.
- Strategy-layer path also includes province color, border distance field, fog of war, flat map, and map fog.
- Representative deep-water pixel output was around `[0.011, 0.018, 0.022, 1.0]`.
- The final output is effectively opaque for the sampled ocean pixels, not a partially transparent cyan overlay.

The important conclusion is that geometry is not the gap. Both paths are sea-level quads. The gap is shader semantics and refraction/pass data.

## Non-Goals

- Do not port `ProvinceColorTexture`, `ProvinceColorIndirectionTexture`, or province color blending.
- Do not port `BorderDistanceFieldTexture` or gradient border rendering.
- Do not port `FogOfWarAlpha`, fog-of-war patterning, or strategy fog.
- Do not port `FlatMapTexture` or flat-map interpolation.
- Do not add coastline meshes or clip the ocean plane against terrain in this slice.
- Do not make Ocean own `sea_level`; sea level remains map settings data supplied through `OceanRuntimeInput`.
- Do not tune the current simplified shader as the main fix.

## Proposed Architecture

Keep the existing Ocean entity, component, processor, render object, and full-map quad.

Extend `OceanRenderFeature` into a two-step water pass:

```text
Scene color + presenter depth
  -> Ocean refraction seed texture
  -> OceanSurface draw
  -> main transparent/scene RT
```

The seed texture mirrors the river refraction seed idea:

- RGB starts from current scene color.
- Alpha stores camera-relative world-distance payload reconstructed from presenter depth.
- Resolution can start at full scene size for correctness; half resolution can be introduced later only if the output is stable.

`OceanSurface.sdsl` should use the refraction seed plus current water textures to implement a CK3-style core water function. This function may reuse or extract logic from `RiverSurface.sdsl`, because river already contains much of the target CK3 water behavior.

## Shader Design

The Ocean shader should move from the current simple model to a core water model with these stages:

1. Compute world UV:

```text
uv.x = world.x / MapWorldSize.x
uv.y = 1 - world.z / MapWorldSize.y
```

2. Sample water color/spec:

```text
WaterColorTexture(worldUv)
```

3. Compute terrain-relative water depth.

First implementation can derive depth from refraction payload:

```text
depth = oceanWorldY - decodedRefractionWorldY
```

If terrain height data is already available to the shader later, a height lookup path can replace or augment this. The first slice should not invent a separate GPU height lookup unless refraction depth proves insufficient.

4. Compute flow normal.

CK3 event `1061` uses flowmap-aligned normal sampling with neighboring flowmap cells and smooth interpolation. The first implementation should copy this structure rather than using the current single moving flow sample:

- sample `FlowMapTexture` at rounded flow-grid coordinates
- decode flow direction and flow strength
- sample `FlowNormalTexture` with derivative-aware UVs
- blend the four neighboring flow normal samples using local cell interpolation

5. Add three ambient wave normals.

Reuse the `RiverSurface` style:

- `_WaterWave1Scale`, `_WaterWave1Rotation`, `_WaterWave1Speed`
- `_WaterWave2Scale`, `_WaterWave2Rotation`, `_WaterWave2Speed`
- `_WaterWave3Scale`, `_WaterWave3Rotation`, `_WaterWave3Speed`
- normal flatten parameters

6. Compute foam.

Use CK3-style foam:

- noise from `FoamNoiseTexture`
- mask from `FoamMapTexture`
- ramp from `FoamRampTexture` with half-texel U clamp
- pattern from `FoamTexture`
- shore mask from water depth using `_WaterFoamShoreMaskDepth` and `_WaterFoamShoreMaskSharpness`

7. Compute refraction.

Use the same semantic structure already present in `RiverSurface`:

- sample base refraction RGB linearly
- read alpha payload with point/load semantics so camera-distance payload is not bilinearly filtered
- decode refraction world position
- compute refraction depth
- compute refraction shore mask
- offset by view-space water normal
- sample offset refraction
- reject offset if it resolves above water
- resample `WaterColorTexture` at refraction world position
- apply see-through attenuation using `_WaterSeeThroughDensity`

8. Compute water fade and reflection.

Follow CK3 core shape:

```text
waterFade = 1 - saturate((WaterFadeShoreMaskDepth - min(surfaceDepth, refractionDepth)) * sharpness)
fresnel = (bias + pow(1 - abs(dot(viewDir, normal)), pow)) * waterFade
final = litWater * waterFade + lerp(refraction, reflection, fresnel)
```

Reflection should use the same reflection cubemap source used by river water, or the shared scene environment if the project decides to consolidate reflection sources. The first implementation should prefer the already working river reflection binding to avoid a second environment path.

9. Output alpha.

Ocean core water should output opaque alpha for normal ocean pixels. The current constant `0.86` should be removed from the target path. If pipeline blending remains enabled for compatibility, the shader output alpha must not make the ocean behave as a bright translucent overlay.

## C# Binding and Render Feature Changes

`OceanRenderFeature` should:

- allocate and maintain an Ocean refraction seed texture sized to the active scene color
- resolve presenter depth as a shader resource, using the same assumptions and asserts as the river seed path
- run a seed image effect before drawing Ocean
- bind the seed texture to `OceanSurface`
- bind `_RefractionTextureSize`, `_ViewSize`, `_ViewMatrix`, `_CameraWorldPosition`, `_GlobalTime`, `_MapWorldSize`, and `_WaterHeight`
- bind existing water textures from `OceanResourceLoader`
- bind reflection cubemap/scene lighting through the existing water lighting path
- use a depth state consistent with CK3-style water overlay: depth enabled, no depth write, strict `Less` if depth bias is required to avoid terrain z-fighting

The implementation should prefer extracting shared helpers from river only when it reduces duplicated shader code and preserves behavior. A separate `WaterCore.sdsl` mixin is acceptable if it keeps river and ocean from diverging.

## Resource Scope

Use existing resources already loaded from `game/map/water`:

- `water_color.dds`
- `ambient_normal.dds`
- `flowmap.dds`
- `flow_normal.dds`
- `foam.dds`
- `foam_ramp.dds`
- `foam_map.dds`
- `foam_noise.dds`

Use existing reflection/scene environment resources rather than adding new CK3-only assets.

No province, border, FOW, flat-map, or strategy overlay textures are required for this design.

## Testing

Automated tests should focus on deterministic contracts:

- `OceanSurface.sdsl` declares and uses `RefractionTexture`.
- `OceanSurface.sdsl` does not contain `ProvinceColor`, `BorderDistanceField`, `FogOfWar`, or `FlatMap`.
- `OceanSurface.sdsl` does not output a hard-coded alpha `0.86`.
- `OceanSurface.sdsl` contains CK3 core water tokens: water fade, see-through, refraction shore mask, foam shore mask, fresnel/reflection.
- `OceanRenderFeature` allocates/binds a refraction seed texture.
- `OceanRenderFeature` binds `_RefractionTextureSize`, `_ViewSize`, `_ViewMatrix`, `_CameraWorldPosition`, `_GlobalTime`, `_WaterHeight`, and `_MapWorldSize`.
- `OceanRenderFeature` uses the Stride shader asset workflow generated keys for new shader parameters.

Shader asset verification is mandatory after SDSL changes:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
```

Then run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
git diff --check
```

## Visual Verification

Use RenderDoc, not only screenshots.

Reference captures:

- Project before: `C:\Users\Redwa\Desktop\debug.rdc`, event `252`
- CK3 target: `C:\Users\Redwa\Desktop\save.rdc`, event `1061`

After implementation, capture a new project frame and compare:

- Ocean draw is still a full-map sea-level quad.
- `OceanSurface` binds refraction texture and all water textures.
- Representative deep-water pixels are no longer HDR-bright cyan.
- Deep-water values should move toward CK3 scale, roughly `0.01..0.07` in the sampled CK3 frame before tone mapping, not `>1.0`.
- Ocean output alpha for normal ocean pixels should be near `1.0`.
- Shoreline water should show depth/fade/foam behavior rather than a flat translucent cyan wash.
- Shader disassembly should show refraction texture sampling, see-through density, water fade shore mask, foam shore mask, and reflection sampling.

## Risks

- Reusing river refraction seed code may expose assumptions about half-resolution payloads and alpha filtering; Ocean should start with correctness over resolution optimization.
- The current scene color before Ocean may still be overbright relative to CK3; refraction and water fade should reduce the water error, but terrain exposure may remain a separate issue.
- Extracting a shared water core can destabilize river if done too aggressively. Keep river behavior unchanged unless tests and RenderDoc prove parity.
- Depth bias and strict depth compare may need tuning for ocean shore overlap with terrain.
- Exact CK3 color values depend on camera, exposure, fog, and strategy overlays that are intentionally out of scope.

## Acceptance Criteria

- The current simplified Ocean color path is replaced by a CK3-style core water path.
- Ocean uses refraction scene payload instead of only direct scene lighting.
- Strategy-layer CK3 overlays remain absent by design.
- SDSL generated keys and Stride asset compile are refreshed successfully.
- Automated tests and solution build pass.
- A fresh RenderDoc capture confirms Ocean shader bindings and output range improved toward CK3 event `1061`.
