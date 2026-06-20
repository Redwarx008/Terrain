# River Stride Lighting Design

**Date:** 2026-06-21  
**Status:** Draft for review  
**Scope:** River bottom and river surface lighting model alignment

## Goal

Make both river bottom and river surface use the same lighting model that Stride standard materials use, while preserving the existing river-specific multi-pass rendering behavior:

- scene seed pass
- river bottom pass
- refraction/bottom intermediate render targets
- river surface pass
- river-specific depth payload, alpha, water fade, foam, refraction, and reflection composition

This is not a rewrite to make river use `MaterialPass`. The river must remain a custom `RiverRenderFeature` because it owns non-standard render targets, blend states, and pass ordering. The change is limited to the lighting response used inside the custom shaders.

## Current Problem

Terrain uses Stride's standard material lighting path:

- `MaterialDiffuseLambertModelFeature`
- `MaterialSpecularMicrofacetModelFeature`
- `ForwardLightingRenderFeature`
- scene directional light and skybox environment light

River currently uses custom CK3-style lighting logic in `RiverBottom.sdsl` and `RiverSurface.sdsl`. This makes river and terrain respond differently to the same scene light. In captures with bright terrain, terrain reaches much higher HDR values while river remains in a lower water-specific energy range.

The target behavior is not to artificially brighten river. The target is to make river bottom and surface use Stride's standard material lighting equations for direct and environment light.

## Stride Source Reference

The relevant Stride engine files are under `E:\WorkSpace\stride`:

- `sources/engine/Stride.Rendering/Rendering/Materials/Shaders/MaterialSurfaceLightingAndShading.sdsl`
- `sources/engine/Stride.Rendering/Rendering/Materials/Shaders/MaterialSurfaceShadingDiffuseLambert.sdsl`
- `sources/engine/Stride.Rendering/Rendering/Materials/Shaders/MaterialSurfaceShadingSpecularMicrofacet.sdsl`
- `sources/engine/Stride.Rendering/Rendering/Materials/Shaders/MaterialPixelStream.sdsl`
- `sources/engine/Stride.Rendering/Rendering/Materials/Shaders/MaterialPixelShadingStream.sdsl`
- `sources/engine/Stride.Rendering/Rendering/Lights/DirectLightGroup.sdsl`
- `sources/engine/Stride.Rendering/Rendering/Lights/LightSkyboxShader.sdsl`
- `sources/engine/Stride.Rendering/Rendering/BRDF/BRDFMicrofacet.sdsl`

Key Stride semantics to preserve:

- `NdotL = max(dot(normalWS, lightDirectionWS), 0.0001f)`
- `lightColorNdotL = lightColor * attenuation * shadow * NdotL * directAO`
- Lambert direct contribution is `diffuse / PI * lightColorNdotL`, then `MaterialSurfaceLightingAndShading` multiplies total direct lighting by `PI`, so the final direct diffuse is equivalent to `diffuse * lightColorNdotL`.
- Microfacet direct specular uses Fresnel, visibility, and normal distribution:
  - `FresnelSchlick(f0, LdotH)`
  - `VisibilitySmithSchlickGGX(alphaRoughness, NdotL, NdotV)`
  - `NormalDistributionGGX(alphaRoughness, NdotH)`
  - combined as `F * V * D / 4 * lightSpecularColorNdotL`, then multiplied by `PI` with direct lighting.
- Environment diffuse is `diffuse * envLightDiffuseColor`.
- Skybox environment samples diffuse from `normalWS` and specular from `reflect(-viewWS, normalWS)`, both multiplied by skybox intensity and ambient accessibility.

## Proposed Architecture

Add one shared river lighting helper shader:

- `Terrain.Editor/Effects/RiverStrideLighting.sdsl`

Both river shaders will use it:

- `RiverBottom.sdsl`
- `RiverSurface.sdsl`

The helper owns the Stride-style lighting equations and exposes functions for:

- direct Lambert diffuse
- direct GGX microfacet specular
- environment diffuse from the scene cubemap
- environment specular from the scene cubemap
- material precomputation equivalent to Stride's `PrepareMaterialForLightingAndShading`

The helper should not own river-specific behavior such as parallax, depth payload packing, refraction, foam, or water fade.

## River Bottom Changes

`RiverBottom.sdsl` keeps:

- bottom diffuse/normal/properties/depth sampling
- tangent-space parallax and normal reconstruction
- bottom alpha calculation
- dual-source output semantics
- camera-relative depth payload behavior

`CalculateRiverBottomLighting` will be replaced or rewritten to call `RiverStrideLighting`:

- bottom diffuse RGB becomes the Stride `matDiffuse` input.
- bottom properties provide roughness/gloss/specular/metalness mapping as currently available.
- direct light uses scene sun direction/color and the existing shadow term.
- environment light uses the scene cubemap, sky matrix, environment intensity, and mip count.

The result should be a Stride-style lit bottom color, not a CK3 custom BRDF.

## River Surface Changes

`RiverSurface.sdsl` keeps:

- flow normal
- ambient water-wave normals
- foam
- water color texture sampling
- refraction
- `WaterFade`
- fresnel/reflection composition
- depth-based alpha

The current surface lighting section using `ImprovedBlinnPhong` and old `ComposeLight` will be replaced with Stride-style lighting:

- `waterDiffuse + foam` becomes the diffuse material color input.
- surface normal is the final water normal.
- surface specular uses the water specular/gloss values already computed from water parameters and water color map.
- direct light and environment light come from scene lighting parameters, not from `_DefaultEnvironmentSunDiffuse`, `_DefaultEnvironmentSunIntensity`, or `_WaterToSunDir` as an independent light path.

The surface still blends lit water with refraction/reflection according to the existing water behavior. Only the lighting model used to light the water material changes.

## C# Parameter Binding

`RiverRenderFeature` already extracts scene lighting for bottom. That path will be generalized so both bottom and surface receive consistent scene lighting parameters:

- `_SceneSunDirection`
- `_SceneSunColor`
- shadow term or shadow data required by the river pass
- `EnvironmentMapTexture`
- `EnvironmentMapSampler`
- `_EnvironmentIntensity`
- `_EnvironmentSkyMatrix`
- `_EnvironmentMipCount`
- camera/world/view data already needed for view direction and refraction

Surface will stop relying on independent default sun parameters as the formal lighting path. Fallback values may remain only for no-scene-light diagnostics and must not be used in the normal configured editor path.

## Testing Plan

Add tests before production shader changes.

Text-level shader tests:

- `RiverSurface.sdsl` no longer uses `ImprovedBlinnPhong` for the main water lighting path.
- `RiverSurface.sdsl` no longer uses the old `ComposeLight` direct/specular composition as the main water lighting path.
- `RiverBottom.sdsl` and `RiverSurface.sdsl` both call the shared Stride-style lighting helper.
- The helper contains the key Stride formulas:
  - `NormalDistributionGGX`
  - `VisibilitySmithSchlickGGX`
  - `FresnelSchlick`
  - final direct diffuse equivalent to Stride Lambert after the direct `PI` restoration.
- `RiverSurface.sdsl` references scene lighting inputs such as `_SceneSunColor`, `_SceneSunDirection`, and `EnvironmentMapTexture`.

C# binding tests:

- `RiverRenderFeature` binds scene sun and scene environment parameters for surface as well as bottom.
- The old surface-only default sun path is not the primary binding path.

Manual RenderDoc verification:

- Capture a new frame after implementation.
- Confirm river bottom and surface shader disassembly contains the new helper path.
- Confirm old `ImprovedBlinnPhong`/old `ComposeLight` does not appear in the active surface path.
- Compare pre-ToneMap HDR values for terrain and river.
- Confirm river refraction, alpha, depth payload, and blend behavior are still intact.

## Stride Asset Workflow

Because this touches SDSL, implementation must follow the Stride shader asset workflow:

1. Verify the shader file is included by the relevant `.sdpkg` asset folders.
2. Verify generated `*.sdsl.cs` key files are updated if shader parameters are added or renamed.
3. Run:

```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

4. Build and run the relevant tests.

## Implementation Order

1. Add failing text tests for the intended shader structure.
2. Add or update C# binding tests for surface scene-light parameter binding.
3. Add `RiverStrideLighting.sdsl`.
4. Port bottom lighting to the helper.
5. Port surface lighting to the helper.
6. Generalize `RiverRenderFeature` scene lighting binding for both bottom and surface.
7. Regenerate shader key files and run the Stride asset rebuild workflow.
8. Run tests.
9. Capture and inspect a new RenderDoc frame.
10. Update architecture/current-feature docs and session logs with the final verified result.

## Non-Goals

- Do not convert river to a standard `MaterialPass`.
- Do not remove the river custom render feature.
- Do not remove refraction, water fade, foam, fresnel reflection, or depth payload behavior.
- Do not solve terrain overbrightness by scaling terrain in this change.
- Do not introduce a one-off brightness multiplier to hide the lighting mismatch.

## Open Risks

- Bit-for-bit parity with Stride generated material shaders is not guaranteed because river remains custom SDSL instead of generated `MaterialPass`.
- Environment specular will initially use Stride's polynomial DFG approximation instead of the LUT-backed path to avoid adding a new material LUT resource. If visual parity still differs after the first implementation, wiring Stride's `EnvironmentLightingDFG_LUT` becomes the next targeted follow-up.
- Bottom and surface material property mappings may need careful calibration because river textures are not authored as ordinary Stride material textures.
- Shadow parity may remain approximate unless the existing river shadow bridge exactly matches Stride direct light shadow streams.

## Implementation Decisions

1. Use a new shared helper file: `Terrain.Editor/Effects/RiverStrideLighting.sdsl`.
2. Use Stride's polynomial GGX environment DFG approximation for the first implementation.
3. Keep fallback default surface sun parameters only for explicit no-scene-light diagnostics, not as the normal editor path.
