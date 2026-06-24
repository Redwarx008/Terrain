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
        AssertContains(shader, "FlowMapTexture.Load", "OceanSurface should load four flowmap cells for unfiltered interpolation");
        AssertContains(shader, "FlowNormalTexture.Sample", "OceanSurface should sample FlowNormalTexture");
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
            "stage float _WaterFlowNormalScale",
            "stage float _WaterFlowNormalFlatten",
            "stage float _WaterFlowTime",
            "stage float2 _WaterFlowMapSize",
            "stage float _WaterFresnelBias",
            "stage float _WaterFresnelPow",
            "stage float _WaterReflectionIntensity",
        ];

        foreach (string parameter in stageParameters)
            AssertContains(shader, parameter, $"OceanSurface should expose {parameter}");

        AssertContains(shader, "ComputeRefractionPayloadCoord", "OceanSurface should compute integer payload coordinates");
        AssertContains(shader, "SampleRefractionPayload", "OceanSurface should isolate unfiltered alpha payload reads");
        AssertContains(shader, "RefractionTexture.Load", "OceanSurface should read refraction alpha payload with Texture2D.Load");
        AssertContains(shader, "DecodeRefractionWorldPosition", "OceanSurface should decode shared refraction payloads");
        AssertContains(shader, "RiverDecompressWorldSpace", "OceanSurface should reuse the river payload decompressor");
        AssertContains(shader, "CalcRefraction", "OceanSurface should keep refraction composition in a named helper");
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
        AssertContains(shader, "float4 flow00", "OceanSurface should sample the first neighboring flowmap cell");
        AssertContains(shader, "float4 flow10", "OceanSurface should sample the second neighboring flowmap cell");
        AssertContains(shader, "float4 flow01", "OceanSurface should sample the third neighboring flowmap cell");
        AssertContains(shader, "float4 flow11", "OceanSurface should sample the fourth neighboring flowmap cell");
        AssertContains(shader, "lerp(lerp(normal00, normal10", "OceanSurface should interpolate the four flow normals");
        AssertContains(shader, "normalMap1 + normalMap2 + normalMap3", "OceanSurface should combine three ambient normal layers");
        AssertContains(shader, "CalcReflection", "OceanSurface should keep reflection in a named helper");
        AssertContains(shader, "EnvironmentMapTexture.SampleLevel", "OceanSurface should sample the shared environment map for reflection");
        AssertContains(shader, "streams.ColorTarget = float4(finalColor, 1.0f);", "OceanSurface should output opaque normal ocean pixels");
        AssertNotContains(shader, "0.86f", "OceanSurface should not keep the old hardcoded translucent alpha");
    }

    private static void OceanShaderUsesSharedRiverStrideLighting()
    {
        string shader = ReadRepositoryText("Terrain/Effects/Ocean/OceanSurface.sdsl");

        AssertContains(shader, "RiverStrideLighting", "OceanSurface should inherit the shared water scene lighting mixin");
        AssertContains(shader, "RiverStrideComputeLighting(", "OceanSurface should use the shared Stride-style lighting helper");
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
