#nullable enable

namespace Terrain.Editor.Tests;

internal static class OceanShaderTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("ocean vertex streams exposes ocean uv", OceanVertexStreamsExposesOceanUv);
        TestHarness.Run("ocean shader samples required water textures", OceanShaderSamplesRequiredWaterTextures);
        TestHarness.Run("ocean shader implements CK3 core water tokens", OceanShaderImplementsCk3CoreWaterTokens);
        TestHarness.Run("ocean shader uses shared river stride lighting", OceanShaderUsesSharedRiverStrideLighting);
        TestHarness.Run("ocean shader avoids strategy-only map tokens", OceanShaderAvoidsStrategyOnlyMapTokens);
        TestHarness.Run("ocean render feature binds resources and scene lighting", OceanRenderFeatureBindsResourcesAndSceneLighting);
        TestHarness.Run("ocean renderer shares refraction clamp with capture", OceanRendererSharesRefractionClampWithCapture);
        TestHarness.Run("ocean render feature does not mutate scene assets", OceanRenderFeatureDoesNotMutateSceneAssets);
        TestHarness.Run("terrain project registers ocean shader key files", TerrainProjectRegistersOceanShaderKeyFiles);
    }

    private static void OceanVertexStreamsExposesOceanUv()
    {
        string shader = ReadRepositoryText("Terrain/Effects/Ocean/OceanVertexStreams.sdsl");

        AssertContains(shader, "stage stream float2 OceanUV : TEXCOORD0;", "OceanVertexStreams should expose the Ocean UV stream on TEXCOORD0");
    }

    private static void OceanShaderSamplesRequiredWaterTextures()
    {
        string shader = ReadRepositoryText("Terrain/Effects/Ocean/OceanSurface.sdsl");

        string[] textureNames =
        [
            "WaterColorTexture",
            "AmbientNormalTexture",
            "FlowMapTexture",
            "FlowNormalTexture",
            "FoamTexture",
            "FoamRampTexture",
            "FoamMapTexture",
            "FoamNoiseTexture",
        ];

        foreach (string textureName in textureNames)
        {
            AssertContains(shader, $"stage Texture2D<float4> {textureName};", $"OceanSurface should declare {textureName}");
        }

        AssertContains(shader, "WaterColorTexture.Sample", "OceanSurface should sample WaterColorTexture");
        AssertContains(shader, "SampleNormalMapTexture(AmbientNormalTexture", "OceanSurface should sample AmbientNormalTexture through the ambient normal helper");
        AssertContains(shader, "FlowMapTexture.SampleLevel", "OceanSurface should sample FlowMapTexture through the CK3 flow normal path");
        AssertContains(shader, "FlowNormalTexture.SampleGrad", "OceanSurface should sample FlowNormalTexture with explicit derivatives");
        AssertContains(shader, "FoamTexture.Sample", "OceanSurface should sample FoamTexture");
        AssertContains(shader, "FoamRampTexture.SampleLevel", "OceanSurface should sample FoamRampTexture");
        AssertContains(shader, "FoamMapTexture.Sample", "OceanSurface should sample FoamMapTexture");
        AssertContains(shader, "FoamNoiseTexture.Sample", "OceanSurface should sample FoamNoiseTexture");

        AssertContains(shader, "stage SamplerState OceanTextureSampler;", "OceanSurface should declare its water texture sampler");
        AssertContains(shader, "stage Texture2D<float4> RefractionTexture;", "OceanSurface should declare the shared refraction texture");
        AssertContains(shader, "stage SamplerState RefractionSampler;", "OceanSurface should declare the shared refraction sampler");
        AssertContains(shader, "stage float2 _RefractionTextureSize = float2(1.0f, 1.0f);", "OceanSurface should expose the shared refraction texture size");
        AssertContains(shader, "RefractionTexture.Sample(RefractionSampler", "OceanSurface should sample the shared refraction capture");
        AssertContains(shader, "shader OceanSurface : ShaderBase, TransformationWAndVP, OceanVertexStreams, RiverStrideLighting", "OceanSurface should use the requested shader mixin chain");
        AssertContains(shader, "stage float4 ShallowColor", "OceanSurface should expose ShallowColor from OceanMaterialSettings");
        AssertContains(shader, "stage float4 DeepColor", "OceanSurface should expose DeepColor from OceanMaterialSettings");
        AssertContains(shader, "stage float _OceanRoughness", "OceanSurface should expose roughness from OceanMaterialSettings");
        AssertContains(shader, "stage float _WaveScale", "OceanSurface should expose wave scale from OceanMaterialSettings");
    }

    private static void OceanShaderImplementsCk3CoreWaterTokens()
    {
        string shader = ReadRepositoryText("Terrain/Effects/Ocean/OceanSurface.sdsl");

        string[] stageParameters =
        [
            "stage float2 _ViewSize",
            "stage float4x4 _ViewMatrix",
            "stage float _WaterSeeThroughDensity",
            "stage float _WaterRefractionScale",
            "stage float _WaterRefractionShoreMaskDepth",
            "stage float _WaterRefractionShoreMaskSharpness",
            "stage float _WaterFadeShoreMaskDepth",
            "stage float _WaterFadeShoreMaskSharpness",
            "stage float _WaterFoamShoreMaskDepth",
            "stage float _WaterFoamShoreMaskSharpness",
            "stage float2 _WaterWave1Scale",
            "stage float2 _WaterWave2Scale",
            "stage float2 _WaterWave3Scale",
            "stage float _OceanWaveSpeedScale",
            "stage float _WaterFlowNormalScale",
            "stage float _WaterFlowNormalFlatten",
            "stage float _OceanFlowSpeedScale",
            "stage float _WaterFlowTime",
            "stage float2 _WaterFlowMapSize",
            "stage float _WaterFresnelBias",
            "stage float _WaterFresnelPow",
            "stage float _WaterReflectionIntensity",
            "stage float _OceanSceneLightingScale",
            "stage float _OceanRefractionColorScale",
            "stage float _OceanWaterColorTextureInfluence",
            "stage float3 _OceanDisplayDeepBase",
            "stage float3 _OceanDisplayDeepReference",
            "stage float _OceanDisplayDeepDetailGain",
            "stage float _OceanDisplayShallowGain",
            "stage float3 _OceanDisplayShallowBias",
            "stage float _OceanDisplayShallowDepth",
            "stage float _OceanDisplayResponseStrength",
        ];

        foreach (string parameter in stageParameters)
            AssertContains(shader, parameter, $"OceanSurface should expose {parameter}");

        AssertContains(shader, "ComputeRefractionPayloadCoord", "OceanSurface should compute integer payload coordinates");
        AssertContains(shader, "SampleRefractionPayload", "OceanSurface should isolate unfiltered alpha payload reads");
        AssertContains(shader, "RefractionTexture.Load", "OceanSurface should read refraction alpha payload with Texture2D.Load");
        AssertContains(shader, "DecodeRefractionWorldPosition", "OceanSurface should decode shared refraction payloads");
        AssertContains(shader, "RiverDecompressWorldSpace", "OceanSurface should reuse the river payload decompressor");
        AssertContains(shader, "CalcRefraction", "OceanSurface should keep refraction composition in a named helper");
        AssertContains(shader, "float2 inverseViewSize = 1.0f / max(_ViewSize, float2(1.0f, 1.0f));", "OceanSurface should scale refraction offset by the active view size");
        AssertContains(shader, "float2 refractionOffset = viewNormal * float2(-inverseViewSize.x, inverseViewSize.y);", "OceanSurface should not hard-code a 1080p refraction offset");
        AssertContains(shader, "float4 baseRefractionSample", "OceanSurface should keep base refraction RGB separate");
        AssertContains(shader, "float unfilteredBasePayload", "OceanSurface should read unfiltered base payload");
        AssertContains(shader, "float4 offsetRefractionSample", "OceanSurface should keep offset refraction RGB separate");
        AssertContains(shader, "float unfilteredOffsetPayload", "OceanSurface should read unfiltered offset payload");
        AssertContains(shader, "step(WorldSpacePos.y, offsetRefractionWorldPosition.y)", "OceanSurface should reject offset samples above water");
        AssertContains(shader, "ComputeWaterFade", "OceanSurface should compute water fade from depth");
        AssertContains(shader, "ComputeRefractionShoreMask", "OceanSurface should compute a refraction shore mask");
        AssertContains(shader, "CalcTerrainUnderwaterSeeThrough", "OceanSurface should apply see-through attenuation");
        AssertContains(shader, "exp(-_WaterSeeThroughDensity", "OceanSurface should attenuate see-through by density");
        AssertContains(shader, "FoamRampTexture.SampleLevel", "OceanSurface should sample foam ramp without mip/filter drift");
        AssertContains(shader, "0.5f / 256.0f", "OceanSurface should clamp foam ramp U by half a texel");
        AssertContains(shader, "1.0f - 0.5f / 256.0f", "OceanSurface should clamp foam ramp upper bound by half a texel");
        AssertContains(shader, "float flowMapScale = 1.5f;", "OceanSurface should use CK3's flow map upscale before blending cells");
        AssertContains(shader, "blendFactor = 0.5f - 4.0f * blendFactor * blendFactor * blendFactor;", "OceanSurface should use CK3's cubic flow blend factor");
        AssertContains(shader, "float4 sample1 = SampleFlowTexture(floor(flowCoord) / flowCoordScale", "OceanSurface should sample the first CK3 flow phase");
        AssertContains(shader, "float4 sample2 = SampleFlowTexture(floor(flowCoord + float2(0.5f, 0.0f)) / flowCoordScale", "OceanSurface should sample the second CK3 flow phase");
        AssertContains(shader, "float4 sample3 = SampleFlowTexture(floor(flowCoord + float2(0.0f, 0.5f)) / flowCoordScale", "OceanSurface should sample the third CK3 flow phase");
        AssertContains(shader, "float4 sample4 = SampleFlowTexture(floor(flowCoord + float2(0.5f, 0.5f)) / flowCoordScale", "OceanSurface should sample the fourth CK3 flow phase");
        AssertContains(shader, "float4 sample12 = lerp(sample2, sample1, blendFactor.x);", "OceanSurface should blend the first two CK3 flow phases");
        AssertContains(shader, "float4 sample = lerp(sample34, sample12, blendFactor.y);", "OceanSurface should blend CK3 flow phases by Y");
        AssertContains(shader, "normal.xz = MulFlowMatrix(flowRotRow0, flowRotRow1, normal.xz);", "OceanSurface should rotate flow normals back into world flow space");
        AssertContains(shader, "CK3 computes gradients from NormalCoord before applying the flow inverse rotation.", "OceanSurface should document the CK3 SampleGrad gradient convention");
        AssertContains(shader, "FlowNormalTexture.SampleGrad(OceanTextureSampler, normalUv, ddxValue, ddyValue);", "OceanSurface should preserve CK3's explicit unrotated flow-normal gradients");
        AssertContains(shader, "normalMap1 + normalMap2 + normalMap3", "OceanSurface should combine three ambient normal layers");
        AssertContains(shader, "CalcReflection", "OceanSurface should keep reflection in a named helper");
        AssertContains(shader, "EnvironmentMapTexture.SampleLevel", "OceanSurface should sample the shared environment map for reflection");
        AssertContains(shader, "stage float _WaterFlowNormalScale = 0.025f;", "OceanSurface should use the CK3 ocean flow-normal scale captured at EID 1061");
        AssertContains(shader, "stage float _WaterDiffuseMultiplier = 0.20f;", "OceanSurface should keep a low diffuse term instead of copying CK3's zero diffuse");
        AssertContains(shader, "stage float _WaterSpecular = 0.01f;", "OceanSurface should default to the captured conservative water specular factor");
        AssertContains(shader, "stage float _WaterGlossBase = 1.15f;", "OceanSurface should default to the captured CK3 water gloss base");
        AssertContains(shader, "stage float _WaterGlossScale = 0.1f;", "OceanSurface should default to the captured CK3 water gloss scale");
        AssertContains(shader, "stage float _WaterReflectionIntensity = 0.10f;", "OceanSurface should keep reflection lower than the earlier bright cyan pass");
        AssertContains(shader, "stage float _OceanSceneLightingScale = 0.18f;", "OceanSurface should apply Ocean-only direct and environment lighting energy scaling");
        AssertContains(shader, "stage float _OceanRefractionColorScale = 0.30f;", "OceanSurface should apply Ocean-only refraction RGB scaling without crushing to CK3's black capture value");
        AssertContains(shader, "stage float _OceanWaterColorTextureInfluence = 0.20f;", "OceanSurface should limit CK3 water color texture dominance under Stride lighting");
        AssertContains(shader, "stage float3 _OceanDisplayDeepBase = float3(0.115f, 0.185f, 0.213f);", "OceanSurface should keep deep-water mean in the CK3 final-display range");
        AssertContains(shader, "stage float3 _OceanDisplayDeepReference = float3(0.065f, 0.128f, 0.146f);", "OceanSurface should amplify deep-water detail around the measured pre-display response");
        AssertContains(shader, "stage float _OceanDisplayDeepDetailGain = 2.0f;", "OceanSurface should restore visible deep-water variation without changing shared refraction");
        AssertContains(shader, "stage float _OceanDisplayShallowGain = 0.8f;", "OceanSurface should keep shallow response based on the original Ocean composition");
        AssertContains(shader, "stage float3 _OceanDisplayShallowBias = float3(0.16f, 0.22f, 0.20f);", "OceanSurface should use a separate shallow-water bias calibrated by RenderDoc hot replacement");
        AssertContains(shader, "stage float _OceanDisplayShallowDepth = 6.0f;", "OceanSurface should avoid applying shallow color response to open water");
        AssertContains(shader, "stage float _OceanDisplayResponseStrength = 1.0f;", "OceanSurface should fully apply the Ocean-only display response by default");
        AssertContains(shader, "stage float _OceanWaveSpeedScale = 0.2f;", "OceanSurface should expose an Ocean-only wave animation speed scale");
        AssertContains(shader, "stage float _OceanFlowSpeedScale = 0.2f;", "OceanSurface should expose an Ocean-only flow animation speed scale");
        AssertContains(shader, "float mappedGlossiness = max(_WaterGlossBase, waterColorAndSpec.a);", "OceanSurface should keep the water gloss base active while reading the water color/spec map alpha");
        AssertContains(shader, "float glossiness = saturate(mappedGlossiness * _WaterGlossScale * saturate(1.0f - _OceanRoughness));", "OceanSurface roughness should remain an active material control");
        AssertContains(shader, "float3 waterDiffuse = textureWaterColor * _WaterDiffuseMultiplier;", "OceanSurface should light the water texture with a low Stride-calibrated diffuse term");
        AssertContains(shader, "float3 scaledLightColorNdotL = RiverStrideGetMainLightColorNdotL(normal, shadow) * _OceanSceneLightingScale;", "OceanSurface should scale direct sun lighting inside the Ocean pass");
        AssertContains(shader, "RiverStrideComputeEnvironmentDiffuse(diffuseColor, normal, _OceanSceneLightingScale)", "OceanSurface should scale environment diffuse with the same Ocean lighting scale");
        AssertContains(shader, "RiverStrideComputeEnvironmentSpecular(specularColor, glossiness, normal, viewDir, _OceanSceneLightingScale)", "OceanSurface should scale environment specular with the same Ocean lighting scale");
        AssertContains(shader, "float waveTime = _GlobalTime * _OceanWaveSpeedScale;", "OceanSurface should slow ambient wave animation without changing global time");
        AssertContains(shader, "float2 offset = float2(0.0f, -_WaterFlowTime * _OceanFlowSpeedScale);", "OceanSurface should slow flow-normal animation without changing CPU time binding");
        AssertContains(shader, "float foamTime = _GlobalTime * _OceanWaveSpeedScale;", "OceanSurface should slow foam animation with the same Ocean-only scale");
        AssertContains(shader, "refractionSample.rgb * _OceanRefractionColorScale", "OceanSurface should scale refraction RGB locally without mutating shared capture");
        AssertContains(shader, "float3 litWater = ComputeOceanLighting(", "OceanSurface should use an Ocean-local lighting wrapper so direct sun is scaled");
        AssertContains(shader, "float3 ApplyOceanDisplayResponse(float3 finalColor, float baseRefractionDepth)", "OceanSurface should keep the final-color compensation isolated to the Ocean pass");
        AssertContains(shader, "float shallowMask = saturate(1.0f - baseRefractionDepth / max(_OceanDisplayShallowDepth, 0.0001f));", "OceanSurface should blend deep and shallow display targets from the unmodified base refraction depth");
        AssertContains(shader, "float3 deepColor = _OceanDisplayDeepBase + (finalColor - _OceanDisplayDeepReference) * _OceanDisplayDeepDetailGain;", "OceanSurface should preserve deep-water variation instead of replacing it with a flat target");
        AssertContains(shader, "float3 shallowColor = finalColor * _OceanDisplayShallowGain + _OceanDisplayShallowBias;", "OceanSurface should keep shallow response tied to original refraction and lighting");
        AssertContains(shader, "max(lerp(deepColor, shallowColor, shallowMask), float3(0.0f, 0.0f, 0.0f))", "OceanSurface should blend the RenderDoc-calibrated deep and shallow responses");
        AssertContains(shader, "lerp(finalColor, displayColor, saturate(_OceanDisplayResponseStrength))", "OceanSurface should expose a strength control for the display response");
        AssertContains(shader, "finalColor = ApplyOceanDisplayResponse(finalColor, baseRefractionDepth);", "OceanSurface should apply the display response after lighting, refraction, and reflection composition");
        AssertContains(shader, "streams.ColorTarget = float4(finalColor, 1.0f);", "OceanSurface should output opaque normal ocean pixels");
        AssertNotContains(shader, "_WaterFlowNormalSpeed", "OceanSurface should use CK3's flow time directly, not an extra flow-normal speed multiplier");
        AssertNotContains(shader, "0.86f", "OceanSurface should not keep the old hardcoded translucent alpha");
        AssertNotContains(shader, "1920.0f", "OceanSurface should not hard-code desktop viewport width in refraction offset");
        AssertNotContains(shader, "1080.0f", "OceanSurface should not hard-code desktop viewport height in refraction offset");
    }

    private static void OceanShaderUsesSharedRiverStrideLighting()
    {
        string shader = ReadRepositoryText("Terrain/Effects/Ocean/OceanSurface.sdsl");

        AssertContains(shader, "RiverStrideLighting", "OceanSurface should inherit the shared water scene lighting mixin");
        AssertContains(shader, "RiverStrideComputeDirectDiffuse(", "OceanSurface should reuse the shared Stride-style direct diffuse helper");
        AssertContains(shader, "RiverStrideComputeDirectSpecular(", "OceanSurface should reuse the shared Stride-style direct specular helper");
        AssertContains(shader, "RiverStrideComputeEnvironmentDiffuse(", "OceanSurface should reuse the shared Stride-style environment diffuse helper");
        AssertContains(shader, "RiverStrideComputeEnvironmentSpecular(", "OceanSurface should reuse the shared Stride-style environment specular helper");
        AssertContains(shader, "RiverStrideEvaluateSceneShadow(", "OceanSurface should use scene shadow inputs supplied by WaterSceneLightingBinder");
        AssertContains(shader, "RiverStrideGetMainLightDirection()", "OceanSurface should derive sun direction from scene lighting");
    }

    private static void OceanShaderAvoidsStrategyOnlyMapTokens()
    {
        string shader = ReadRepositoryText("Terrain/Effects/Ocean/OceanSurface.sdsl");
        string feature = ReadRepositoryText("Terrain/Rendering/Ocean/OceanRenderFeature.cs");
        string combined = shader + "\n" + feature;

        string[] forbidden =
        [
            "ProvinceColor",
            "BorderDistanceField",
            "PatternTexture",
            "FogOfWar",
            "FlatMap",
            "_WaterToSunDir",
            "_DefaultEnvironmentSun",
        ];

        foreach (string token in forbidden)
            AssertNotContains(combined, token, $"Ocean implementation should not use CK3-only token {token}");
    }

    private static void OceanRenderFeatureBindsResourcesAndSceneLighting()
    {
        string feature = ReadRepositoryText("Terrain/Rendering/Ocean/OceanRenderFeature.cs");

        AssertContains(feature, "public override Type SupportedRenderObjectType => typeof(OceanRenderObject);", "OceanRenderFeature should only support OceanRenderObject");
        AssertContains(feature, "SortKey = 180;", "OceanRenderFeature should sort slightly before RiverRenderFeature");
        AssertContains(feature, "new DynamicEffectInstance(\"OceanSurface\")", "OceanRenderFeature should initialize the OceanSurface shader");
        AssertContains(feature, "private readonly OceanResourceLoader oceanResources = new();", "OceanRenderFeature should own the ocean resource loader");
        AssertContains(feature, "if (oceanEffect == null || !oceanResources.IsLoaded)", "OceanRenderFeature should skip Prepare when required ocean textures are missing");
        AssertContains(feature, "if (oceanEffect == null || pipelineState == null || !oceanResources.IsLoaded || startIndex >= endIndex)", "OceanRenderFeature should skip Draw when required ocean textures are missing");
        AssertContains(feature, "internal void DrawWater(", "OceanRenderFeature should expose the renderer-callable water draw entry point");
        AssertContains(feature, "Texture sharedRefractionTexture", "OceanRenderFeature.DrawWater should receive the shared refraction capture");
        AssertContains(feature, "float refractionMaxCameraHeight", "OceanRenderFeature.DrawWater should receive the same refraction clamp used by capture");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionTexture, sharedRefractionTexture);", "OceanRenderFeature should bind the shared refraction texture");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionSampler, context.GraphicsDevice.SamplerStates.LinearClamp);", "OceanRenderFeature should bind the shared refraction sampler");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys._RefractionTextureSize, new Vector2(refractionWidth, refractionHeight));", "OceanRenderFeature should bind the shared refraction dimensions");
        AssertContains(feature, "oceanEffect.Parameters.Set(RiverCommonKeys._RefractionMaxCameraHeight, refractionMaxCameraHeight);", "OceanRenderFeature should bind the shared refraction decompression clamp");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys._WaterFlowTime, waterFlowTime);", "OceanRenderFeature should bind water flow time each frame");
        AssertContains(feature, "private WaterSceneLightingBinder? sceneLightingBinder;", "OceanRenderFeature should reuse the shared water scene lighting binder");
        AssertContains(feature, "sceneLightingBinder = new WaterSceneLightingBinder(this, forwardLightingFeature, shadowMapRenderer);", "OceanRenderFeature should initialize the shared lighting binder");
        AssertContains(feature, "sceneLightingBinder?.Bind(context, renderView, oceanEffect);", "OceanRenderFeature should bind scene lighting before drawing");
        AssertContains(feature, "ApplyOceanParameters(oceanEffect, oceanParametersSource, material);", "OceanRenderFeature should bind pass-wide ocean parameters during Prepare");
        AssertContains(feature, "effect.Parameters.Set(OceanSurfaceKeys.ShallowColor", "OceanRenderFeature should bind ShallowColor");
        AssertContains(feature, "effect.Parameters.Set(OceanSurfaceKeys.DeepColor", "OceanRenderFeature should bind DeepColor");
        AssertContains(feature, "effect.Parameters.Set(OceanSurfaceKeys._OceanRoughness", "OceanRenderFeature should bind roughness");
        AssertContains(feature, "effect.Parameters.Set(OceanSurfaceKeys._WaveScale", "OceanRenderFeature should bind wave scale");
        AssertContains(feature, "effect.Parameters.Set(OceanSurfaceKeys._WaterHeight, oceanObject.SeaLevel);", "OceanRenderFeature should bind sea level from runtime input");
        AssertContains(feature, "effect.Parameters.Set(OceanSurfaceKeys._MapWorldSize, oceanObject.MapWorldSize);", "OceanRenderFeature should bind map world size from runtime input");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys._CameraWorldPosition", "OceanRenderFeature should bind camera position each frame");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys._GlobalTime", "OceanRenderFeature should bind global time each frame");
        AssertContains(feature, "effect.Parameters.Set(TransformationKeys.ViewProjection, renderView.ViewProjection);", "OceanRenderFeature should bind view-projection");
        AssertContains(feature, "effect.Parameters.Set(OceanSurfaceKeys._ViewSize, renderView.ViewSize);", "OceanRenderFeature should bind the shader-local view size");
        AssertContains(feature, "effect.Parameters.Set(OceanSurfaceKeys._ViewMatrix, renderView.View);", "OceanRenderFeature should bind the view matrix for view-space refraction");
        AssertContains(feature, "oceanEffect.Parameters.Set(TransformationKeys.World, oceanObject.World);", "OceanRenderFeature should bind world matrix per object");
        AssertContains(feature, "oceanEffect.Parameters.Set(TransformationKeys.WorldView, oceanObject.World * renderView.View);", "OceanRenderFeature should bind world-view matrix per object");
        AssertContains(feature, "oceanEffect.Parameters.Set(TransformationKeys.WorldViewProjection, oceanObject.World * renderView.ViewProjection);", "OceanRenderFeature should bind world-view-projection matrix per object");
        AssertContains(feature, "pipelineState.State.RootSignature = oceanEffect.RootSignature;", "OceanRenderFeature should bind pipeline state from the dynamic effect");
        AssertContains(feature, "commandList.SetPipelineState(pipelineState.CurrentState);", "OceanRenderFeature should bind the pipeline before drawing");
        AssertContains(feature, "commandList.DrawIndexed(oceanObject.IndexCount);", "OceanRenderFeature should draw OceanRenderObject indices");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys.OceanTextureSampler, graphicsDevice.SamplerStates.LinearWrap);", "OceanRenderFeature should bind the ocean texture sampler");
        AssertContains(feature, "oceanEffect.Parameters.Set(RiverStrideLightingKeys.EnvironmentMapSampler, graphicsDevice.SamplerStates.LinearClamp);", "OceanRenderFeature should bind the shared environment map sampler");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys._WaterFlowMapSize, new Vector2(oceanResources.FlowMap.ViewWidth, oceanResources.FlowMap.ViewHeight));", "OceanRenderFeature should bind the actual loaded flowmap dimensions");

        string[] resourceProperties =
        [
            "WaterColor",
            "AmbientNormal",
            "FlowMap",
            "FlowNormal",
            "Foam",
            "FoamRamp",
            "FoamMap",
            "FoamNoise",
        ];

        foreach (string property in resourceProperties)
            AssertContains(feature, $"oceanResources.{property}", $"OceanRenderFeature should bind OceanResourceLoader.{property}");

        AssertNotContains(feature, "NormalStrength", "OceanRenderFeature should not bind a non-existent ocean material field");
        AssertNotContains(feature, "FoamIntensity", "OceanRenderFeature should not bind a non-existent ocean material field");
    }

    private static void OceanRendererSharesRefractionClampWithCapture()
    {
        string renderer = ReadRepositoryText("Terrain/Rendering/CustomForwardRenderer.cs");

        AssertContains(renderer, "float refractionMaxCameraHeight = ResolveWaterRefractionMaxCameraHeight();", "CustomForwardRenderer should compute one shared refraction clamp");
        AssertContains(renderer, "waterRefractionCapturePass.Capture(", "CustomForwardRenderer should pass the clamp into the shared capture");
        AssertContains(renderer, "refractionMaxCameraHeight);", "CustomForwardRenderer should bind the clamp when creating the shared capture");
        AssertContains(renderer, "float refractionMaxCameraHeight = capture.RefractionMaxCameraHeight;", "CustomForwardRenderer should reuse the capture clamp for Ocean draw");
        AssertContains(renderer, "refractionMaxCameraHeight);", "CustomForwardRenderer should pass the shared clamp into OceanRenderFeature.DrawWater");
    }

    private static void OceanRenderFeatureDoesNotMutateSceneAssets()
    {
        string feature = ReadRepositoryText("Terrain/Rendering/Ocean/OceanRenderFeature.cs");

        AssertNotContains(feature, "GraphicsCompositor", "OceanRenderFeature should not mutate graphics compositor assets at runtime");
        AssertNotContains(feature, "MainScene", "OceanRenderFeature should not touch scene assets");
    }

    private static void TerrainProjectRegistersOceanShaderKeyFiles()
    {
        string project = ReadRepositoryText("Terrain/Terrain.csproj");

        AssertContains(project, "<Compile Update=\"Effects\\Ocean\\OceanVertexStreams.sdsl.cs\">", "Terrain.csproj should compile generated OceanVertexStreams keys");
        AssertContains(project, "<Compile Update=\"Effects\\Ocean\\OceanSurface.sdsl.cs\">", "Terrain.csproj should compile generated OceanSurface keys");
        AssertContains(project, "<None Update=\"Effects\\**\\*.sdsl\" Generator=\"StrideShaderKeyGenerator\" />", "Terrain.csproj should keep SDSL files registered for Stride shader key generation");
        AssertContains(project, "<None Update=\"Effects\\**\\*.sdfx\" Generator=\"StrideShaderKeyGenerator\" />", "Terrain.csproj should keep SDFX files registered for Stride shader key generation");
        AssertNotContains(project, "<AdditionalFiles Include=\"Effects\\**\\*.sdsl\" />", "Terrain.csproj should not move shader sources out of the Stride None item pipeline");
        AssertNotContains(project, "<LastGenOutput>OceanVertexStreams.sdsl.cs</LastGenOutput>", "Terrain.csproj should not use legacy LastGenOutput metadata for OceanVertexStreams");
        AssertNotContains(project, "<LastGenOutput>OceanSurface.sdsl.cs</LastGenOutput>", "Terrain.csproj should not use legacy LastGenOutput metadata for OceanSurface");
    }

    private static string ReadRepositoryText(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Terrain.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        TestHarness.Assert(source.Contains(expected, StringComparison.Ordinal), message);
    }

    private static void AssertNotContains(string source, string unexpected, string message)
    {
        TestHarness.Assert(!source.Contains(unexpected, StringComparison.Ordinal), message);
    }
}
