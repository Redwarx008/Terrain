#nullable enable

namespace Terrain.Editor.Tests;

internal static class OceanShaderTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("ocean vertex streams exposes ocean uv", OceanVertexStreamsExposesOceanUv);
        TestHarness.Run("ocean shader samples required water textures", OceanShaderSamplesRequiredWaterTextures);
        TestHarness.Run("ocean shader uses shared river stride lighting", OceanShaderUsesSharedRiverStrideLighting);
        TestHarness.Run("ocean shader avoids strategy-only map tokens", OceanShaderAvoidsStrategyOnlyMapTokens);
        TestHarness.Run("ocean render feature binds resources and scene lighting", OceanRenderFeatureBindsResourcesAndSceneLighting);
        TestHarness.Run("ocean render feature stays out of scene and compositor assets", OceanRenderFeatureStaysOutOfSceneAndCompositorAssets);
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
            AssertContains(shader, $"{textureName}.Sample", $"OceanSurface should sample {textureName}");
        }

        AssertContains(shader, "stage SamplerState OceanTextureSampler;", "OceanSurface should declare its water texture sampler");
        AssertContains(shader, "shader OceanSurface : ShaderBase, TransformationWAndVP, OceanVertexStreams, RiverStrideLighting", "OceanSurface should use the requested shader mixin chain");
        AssertContains(shader, "stage float4 ShallowColor", "OceanSurface should expose ShallowColor from OceanMaterialSettings");
        AssertContains(shader, "stage float4 DeepColor", "OceanSurface should expose DeepColor from OceanMaterialSettings");
        AssertContains(shader, "stage float _OceanRoughness", "OceanSurface should expose roughness from OceanMaterialSettings");
        AssertContains(shader, "stage float _WaveScale", "OceanSurface should expose wave scale from OceanMaterialSettings");
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
            "ProvinceColorTexture",
            "BorderDistanceField",
            "PatternTexture",
            "FogOfWarAlpha",
            "FlatMapTexture",
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
        AssertContains(feature, "oceanEffect.Parameters.Set(TransformationKeys.World, oceanObject.World);", "OceanRenderFeature should bind world matrix per object");
        AssertContains(feature, "oceanEffect.Parameters.Set(TransformationKeys.WorldView, oceanObject.World * renderView.View);", "OceanRenderFeature should bind world-view matrix per object");
        AssertContains(feature, "oceanEffect.Parameters.Set(TransformationKeys.WorldViewProjection, oceanObject.World * renderView.ViewProjection);", "OceanRenderFeature should bind world-view-projection matrix per object");
        AssertContains(feature, "pipelineState.State.RootSignature = oceanEffect.RootSignature;", "OceanRenderFeature should bind pipeline state from the dynamic effect");
        AssertContains(feature, "commandList.SetPipelineState(pipelineState.CurrentState);", "OceanRenderFeature should bind the pipeline before drawing");
        AssertContains(feature, "commandList.DrawIndexed(oceanObject.IndexCount);", "OceanRenderFeature should draw OceanRenderObject indices");
        AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys.OceanTextureSampler, graphicsDevice.SamplerStates.LinearWrap);", "OceanRenderFeature should bind the ocean texture sampler");
        AssertContains(feature, "oceanEffect.Parameters.Set(RiverStrideLightingKeys.EnvironmentMapSampler, graphicsDevice.SamplerStates.LinearClamp);", "OceanRenderFeature should bind the shared environment map sampler");

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

    private static void OceanRenderFeatureStaysOutOfSceneAndCompositorAssets()
    {
        string feature = ReadRepositoryText("Terrain/Rendering/Ocean/OceanRenderFeature.cs");
        string compositor = ReadRepositoryText("Terrain/Assets/GraphicsCompositor.sdgfxcomp");
        string mainScene = ReadRepositoryText("Terrain/Assets/MainScene.sdscene");

        AssertNotContains(feature, "GraphicsCompositor", "OceanRenderFeature should not mutate graphics compositor assets at runtime");
        AssertNotContains(feature, "MainScene", "OceanRenderFeature should not touch scene assets");
        AssertNotContains(compositor, "OceanRenderFeature", "Task 8 should not register OceanRenderFeature in GraphicsCompositor");
        AssertNotContains(mainScene, "OceanComponent", "Task 8 should not add an OceanComponent to MainScene");
        AssertContains(mainScene, "OceanEntity: null", "MainScene should keep the existing no-ocean runtime placeholder for Task 9");
    }

    private static void TerrainProjectRegistersOceanShaderKeyFiles()
    {
        string project = ReadRepositoryText("Terrain/Terrain.csproj");

        AssertContains(project, "<Compile Update=\"Effects\\Ocean\\OceanVertexStreams.sdsl.cs\">", "Terrain.csproj should compile generated OceanVertexStreams keys");
        AssertContains(project, "<Compile Update=\"Effects\\Ocean\\OceanSurface.sdsl.cs\">", "Terrain.csproj should compile generated OceanSurface keys");
        AssertContains(project, "<None Update=\"Effects\\Ocean\\OceanVertexStreams.sdsl\">", "Terrain.csproj should register OceanVertexStreams for key generation");
        AssertContains(project, "<None Update=\"Effects\\Ocean\\OceanSurface.sdsl\">", "Terrain.csproj should register OceanSurface for key generation");
        AssertContains(project, "<LastGenOutput>OceanVertexStreams.sdsl.cs</LastGenOutput>", "Terrain.csproj should map OceanVertexStreams generated output");
        AssertContains(project, "<LastGenOutput>OceanSurface.sdsl.cs</LastGenOutput>", "Terrain.csproj should map OceanSurface generated output");
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
