namespace Terrain.Editor.Tests;

internal static class RiverShaderTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("river ck3 texture assets have correct Stride color-space flags", Ck3TextureAssetsHaveStrideDescriptors);
        TestHarness.Run("river ck3 texture assets are bundle roots for dynamic loading", Ck3TextureAssetsAreBundleRoots);
        TestHarness.Run("river bottom shader samples texture assets", BottomShaderSamplesTextureAssets);
        TestHarness.Run("river common shader uses ck3 river depth profile", CommonShaderUsesCk3RiverDepthProfile);
        TestHarness.Run("river bottom shader uses ck3 advanced parallax semantics", BottomShaderUsesCk3AdvancedParallaxSemantics);
        TestHarness.Run("river bottom shader uses ck3 advanced bank fade alpha", BottomShaderUsesCk3AdvancedBankFadeAlpha);
        TestHarness.Run("river bottom shader uses ck3-style material lighting", BottomShaderUsesCk3StyleMaterialLighting);
        TestHarness.Run("river render objects carry settings into shader binding", RenderObjectCarriesRiverSettingsToShaderBinding);
        TestHarness.Run("river surface shader samples water texture assets", SurfaceShaderSamplesWaterTextureAssets);
        TestHarness.Run("river surface shader follows ck3 water color and refraction semantics", SurfaceShaderFollowsCk3WaterColorAndRefractionSemantics);
        TestHarness.Run("river render feature separates scene seed from working refraction buffer", RenderFeatureSeparatesSceneSeedFromWorkingBuffer);
        TestHarness.Run("river bottom shader packs refraction distance for surface see through", BottomShaderPacksRefractionDistanceForSurfaceSeeThrough);
        TestHarness.Run("river resource loader does not silently ignore missing textures", ResourceLoaderDoesNotSilentlyIgnoreMissingTextures);
    }

    private static void Ck3TextureAssetsHaveStrideDescriptors()
    {
        (string DescriptorPath, string SourcePath, bool UseSRgbSampling)[] assets =
        [
            ("Terrain.Editor/Assets/River/Bottom/bottom-diffuse.sdtex", "bottom-diffuse.dds", true),
            ("Terrain.Editor/Assets/River/Bottom/bottom-normal.sdtex", "bottom-normal.dds", false),
            ("Terrain.Editor/Assets/River/Bottom/bottom-properties.sdtex", "bottom-properties.dds", false),
            ("Terrain.Editor/Assets/River/Bottom/bottom-depth.sdtex", "bottom-depth.dds", false),
            ("Terrain.Editor/Assets/River/Water/ambient-normal.sdtex", "ambient-normal.dds", false),
            ("Terrain.Editor/Assets/River/Water/flow-normal.sdtex", "flow-normal.dds", false),
            ("Terrain.Editor/Assets/River/Water/foam.sdtex", "foam.dds", false),
            ("Terrain.Editor/Assets/River/Water/foam-ramp.sdtex", "foam-ramp.dds", false),
            ("Terrain.Editor/Assets/River/Water/foam-map.sdtex", "foam-map.dds", false),
            ("Terrain.Editor/Assets/River/Water/foam-noise.sdtex", "foam-noise.dds", false),
            ("Terrain.Editor/Assets/River/Water/water-color.sdtex", "water-color.dds", true),
            ("Terrain.Editor/Assets/River/Environment/reflection-specular.sdtex", "reflection-specular.dds", false),
        ];

        foreach (var (descriptorPath, sourcePath, useSRgbSampling) in assets)
        {
            string fullDescriptorPath = GetRepositoryPath(descriptorPath);
            TestHarness.Assert(File.Exists(fullDescriptorPath), $"{descriptorPath} should exist so Content.Load can resolve River texture assets");

            string descriptor = File.ReadAllText(fullDescriptorPath);
            AssertContains(descriptor, "!Texture", $"{descriptorPath} should be a Stride texture asset");
            AssertContains(descriptor, sourcePath, $"{descriptorPath} should point at {sourcePath}");
            AssertContains(descriptor, "!ColorTextureType", $"{descriptorPath} should import CK3 packed DDS channels without normal-map conversion");
            AssertContains(
                descriptor,
                $"UseSRgbSampling: {useSRgbSampling.ToString().ToLowerInvariant()}",
                $"{descriptorPath} should preserve the approved CK3 color-space sampling mode");

            string fullSourcePath = Path.Combine(Path.GetDirectoryName(fullDescriptorPath)!, sourcePath);
            TestHarness.Assert(File.Exists(fullSourcePath), $"{sourcePath} should exist next to {descriptorPath}");
        }
    }

    private static void Ck3TextureAssetsAreBundleRoots()
    {
        string package = ReadRepositoryText("Terrain.Editor/Terrain.Editor.sdpkg");

        string[] urls =
        [
            "River/Bottom/bottom-diffuse",
            "River/Bottom/bottom-normal",
            "River/Bottom/bottom-properties",
            "River/Bottom/bottom-depth",
            "River/Water/ambient-normal",
            "River/Water/flow-normal",
            "River/Water/foam",
            "River/Water/foam-ramp",
            "River/Water/foam-map",
            "River/Water/foam-noise",
            "River/Water/water-color",
            "River/Environment/reflection-specular",
        ];

        foreach (string url in urls)
        {
            AssertContains(package, $":{url}", $"{url} should be a RootAsset so dynamic Content.Load includes it in the bundle");
        }
    }

    private static void BottomShaderSamplesTextureAssets()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "BottomDiffuseTexture", "RiverBottom should declare the bottom diffuse texture");
        AssertContains(shader, "BottomNormalTexture", "RiverBottom should declare the bottom normal texture");
        AssertContains(shader, "BottomPropertiesTexture", "RiverBottom should declare the bottom properties texture");
        AssertContains(shader, "BottomDepthTexture", "RiverBottom should declare the bottom depth texture");
        AssertContains(shader, "BottomDiffuseTexture.Sample", "RiverBottom should sample bottom diffuse instead of relying on constant color");
        AssertContains(shader, "BottomNormalTexture.Sample", "RiverBottom should sample bottom normal for riverbed detail");
        AssertContains(shader, "BottomPropertiesTexture.Sample", "RiverBottom should sample bottom properties for riverbed material response");
        AssertContains(shader, "BottomDepthTexture.Sample", "RiverBottom should sample bottom depth for parallax/depth shaping");
    }

    private static void CommonShaderUsesCk3RiverDepthProfile()
    {
        string common = ReadRepositoryText("Terrain.Editor/Effects/RiverCommon.sdsl");

        AssertContains(common, "cos(crossSection * 2.0f * 3.14159265f) * 0.5f + 0.5f", "River depth should use CK3's cosine-shaped cross-section profile");
        AssertContains(common, "1.0f - pow(saturate(cosine), max(depthWidthPower, 0.0001f))", "River depth should preserve CK3's cosine profile through the depth-width power");
        AssertNotContains(common, "abs(crossSection * 2.0f - 1.0f)", "River depth should not regress to the older parabolic ribbon profile");
    }

    private static void BottomShaderUsesCk3AdvancedParallaxSemantics()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "stage float _TextureUvScale", "RiverBottom should expose CK3 texture-UV scaling");
        AssertContains(shader, "stage float _OceanFadeRate", "RiverBottom should expose CK3 ocean fade shaping");
        AssertContains(shader, "stage float _BankAmount", "RiverBottom should expose CK3 bank amount shaping");
        AssertContains(shader, "CalcBottomDepth(float2 tangentUv, float4 bottomNormalSample)", "RiverBottom should compute bottom depth from tangent-space UVs and normal data");
        AssertContains(shader, "void CalculateParallaxOffsetSteep(", "RiverBottom should implement CK3-style steep parallax stepping");
        AssertContains(shader, "void CalcParallaxedBottomUvs(", "RiverBottom should derive tangent/world UVs through a dedicated parallax helper");
        AssertContains(shader, "float2 scaledRiverUv = float2(riverUv.x * _TextureUvScale, riverUv.y);", "RiverBottom should scale river ribbon UVs with _TextureUvScale before parallax");
        AssertContains(shader, "BottomDiffuseTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom should sample bottom diffuse in tangent UV space");
        AssertContains(shader, "BottomNormalTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom should sample bottom normal in tangent UV space");
        AssertContains(shader, "BottomPropertiesTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom should sample bottom properties in tangent UV space");
        AssertNotContains(shader, "float2 ComputeBottomWorldUv(float3 worldPosition)", "RiverBottom should no longer centralize bottom sampling around world-space UVs");
    }

    private static void BottomShaderUsesCk3AdvancedBankFadeAlpha()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "underOceanFade", "RiverBottom should compute CK3-style underwater fade before bottom alpha");
        AssertContains(shader, "fadeOut", "RiverBottom should use a dedicated fadeOut term for bottom blending");
        AssertContains(shader, "edgeFade1", "RiverBottom should expose the first explicit bank fade term");
        AssertContains(shader, "edgeFade2", "RiverBottom should expose the second explicit bank fade term");
        AssertContains(shader, "float alpha = bottomDiffuse.a * fadeOut * connectionFade * edgeFade1 * edgeFade2;", "RiverBottom alpha should be driven by diffuse alpha and explicit CK3 bank fades");
        AssertNotContains(shader, "float bottomEdgeFade = saturate(depth * 13.0f);", "RiverBottom should not keep the previous depth*13 edge fade shortcut");
    }

    private static void BottomShaderUsesCk3StyleMaterialLighting()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");

        AssertContains(shader, "TextureCube<float4> EnvironmentMapTexture", "RiverBottom should bind a cube environment map like CK3 bottom lighting");
        AssertContains(shader, "EnvironmentMapTexture.SampleLevel", "RiverBottom should sample environment IBL instead of baking brightness into diffuse");
        AssertContains(shader, "CalculateRiverBottomLighting", "RiverBottom should route bottom albedo through a material lighting function");
        AssertContains(shader, "_BottomSunDirection", "RiverBottom should have an explicit directional light input for direct lighting");
        AssertContains(shader, "_ShadowTermFallback", "RiverBottom should keep shadow as a lighting input rather than a diffuse multiplier");
        AssertContains(shader, "RiverUnpackRRxGNormal", "RiverBottom should use CK3's RRxG normal packing for bottom normals");
        AssertContains(shader, "float3x3 tbn", "RiverBottom should transform the sampled bottom normal through the river TBN");
        AssertContains(feature, "RiverBottomKeys.EnvironmentMapTexture", "RiverRenderFeature should bind the environment cubemap to the bottom pass");
        AssertNotContains(shader, "_BottomDiffuseMultiplier", "RiverBottom should not use a brightness multiplier to hide missing lighting");
        AssertNotContains(shader, "bottomDiffuse.rgb * depthTint * 2.0f", "RiverBottom should not darken albedo by depth tint as a substitute for CK3 material lighting");
    }

    private static void RenderObjectCarriesRiverSettingsToShaderBinding()
    {
        string renderObject = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderObject.cs");
        string processor = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverProcessor.cs");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");

        AssertContains(renderObject, "public float TextureUvScale", "RiverRenderObject should cache texture UV scale for shader binding");
        AssertContains(renderObject, "public float OceanFadeRate", "RiverRenderObject should cache ocean fade rate for shader binding");
        AssertContains(renderObject, "public float BankAmount", "RiverRenderObject should cache bank amount for shader binding");
        AssertContains(renderObject, "public int ParallaxIterations", "RiverRenderObject should cache parallax iteration count for shader binding");
        AssertContains(renderObject, "ApplySettings(RiverRenderSettings settings)", "RiverRenderObject should snapshot river settings from the component");
        AssertContains(processor, "renderObject.ApplySettings(component.Settings);", "RiverProcessor should push component settings into each render object");
        AssertContains(feature, "RiverBottomKeys._TextureUvScale", "RiverRenderFeature should bind bottom texture UV scale");
        AssertContains(feature, "RiverBottomKeys._BankAmount", "RiverRenderFeature should bind bottom bank amount");
        AssertContains(feature, "RiverBottomKeys._OceanFadeRate", "RiverRenderFeature should bind bottom ocean fade rate");
        AssertContains(feature, "RiverBottomKeys._ParallaxIterations", "RiverRenderFeature should bind bottom parallax iteration count");
        AssertContains(feature, "RiverSurfaceKeys._FlowNormalUvScale", "RiverRenderFeature should bind surface flow-normal UV scale from settings");
        AssertContains(feature, "RiverSurfaceKeys.WaterColorShallow", "RiverRenderFeature should bind shallow water color from settings");
    }

    private static void SurfaceShaderSamplesWaterTextureAssets()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");

        AssertContains(shader, "AmbientNormalTexture", "RiverSurface should declare ambient normal texture");
        AssertContains(shader, "FlowNormalTexture", "RiverSurface should declare flow normal texture");
        AssertContains(shader, "FoamTexture", "RiverSurface should declare foam texture");
        AssertContains(shader, "FoamRampTexture", "RiverSurface should declare foam ramp texture");
        AssertContains(shader, "FoamMapTexture", "RiverSurface should declare foam map texture");
        AssertContains(shader, "FoamNoiseTexture", "RiverSurface should declare foam noise texture");
        AssertContains(shader, "ReflectionSpecularTexture", "RiverSurface should declare reflection/specular texture");
        AssertContains(shader, "WaterColorTexture", "RiverSurface should declare CK3 water color/spec texture");
        AssertContains(shader, "FlowNormalTexture.Sample", "RiverSurface should use texture-driven flow normals");
        AssertContains(shader, "AmbientNormalTexture.Sample", "RiverSurface should use ambient normal ripples");
        AssertContains(shader, "WaterColorTexture.Sample", "RiverSurface should use CK3 water color/spec texture");
        AssertContains(shader, "FoamNoiseTexture.Sample", "RiverSurface should use foam noise");
        AssertContains(shader, "FoamMapTexture.Sample", "RiverSurface should use foam map shaping");
        AssertContains(shader, "FoamTexture.Sample", "RiverSurface should use foam detail texture");
        AssertContains(shader, "FoamRampTexture.Sample", "RiverSurface should ramp foam intensity");
        AssertContains(shader, "ReflectionSpecularTexture.Sample", "RiverSurface should add asset-driven specular/reflection variation");
    }

    private static void SurfaceShaderFollowsCk3WaterColorAndRefractionSemantics()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");

        AssertContains(shader, "float2 ComputeMapWorldUv(float3 worldPosition)", "RiverSurface should centralize CK3 map-space UV conversion");
        AssertContains(shader, "uv.y = 1.0f - uv.y;", "RiverSurface should flip map-space Y like CK3 before sampling water-color maps");
        AssertContains(shader, "float2 worldUv = ComputeMapWorldUv(streams.PositionWS.xyz);", "RiverSurface should sample surface water color/spec in CK3 map world UV space");
        AssertContains(shader, "float4 waterColorAndSpec = WaterColorTexture.Sample(WaterTextureSampler, worldUv);", "RiverSurface should sample CK3 water color/spec texture in map world UV space");
        AssertContains(shader, "float glossMap = waterColorAndSpec.a;", "RiverSurface should use CK3 water-color alpha as the gloss/spec map");
        AssertContains(shader, "float2 refractionWorldUv = ComputeMapWorldUv(refractionWorldPosition);", "RiverSurface should sample refraction tint at the refracted bottom world position like CK3");
        AssertContains(shader, "float4 refractionWaterColorAndSpec = WaterColorTexture.Sample(WaterTextureSampler, refractionWorldUv);", "RiverSurface should resample CK3 water-color at the accepted refraction world position");
        AssertContains(shader, "float3 refractionWaterColorMap = lerp(refractionWaterColorAndSpec.rgb, _WaterColorMapTint, saturate(_WaterColorMapTintFactor));", "RiverSurface should treat refraction water-color RGB as the see-through tint map");
        AssertContains(shader, "float4 refractionResult = SampleRefractionSeeThrough(screenUv, refractionOffset, streams.PositionWS.xyz, worldDepth);", "RiverSurface should compute CK3-style see-through refraction from the bottom buffer");
        AssertContains(shader, "float3 refractionColor = refractionResult.rgb;", "RiverSurface should keep refraction color separate from the depth used for shore fade");
        AssertContains(shader, "float refractionFadeDepth = refractionResult.a;", "RiverSurface should use the same refraction depth as CK3 for water fade");
        AssertContains(shader, "float4 baseRefraction = RefractionTexture.Sample(RefractionSampler, saturate(screenUv));", "RiverSurface should first sample the undistorted bottom buffer like CK3");
        AssertContains(shader, "float4 distortedRefraction = RefractionTexture.Sample(RefractionSampler, saturate(screenUv + refractionOffset));", "RiverSurface should sample distorted refraction separately from the base sample");
        AssertContains(shader, "float useDistortedRefraction = (distortedRefraction.a > 0.0001f && distortedWorldPosition.y <= surfaceWorldPosition.y) ? 1.0f : 0.0f;", "RiverSurface should reject distorted refraction samples that land outside the bottom buffer or above the water surface");
        AssertContains(shader, "float4 refraction = lerp(baseRefraction, distortedRefraction, useDistortedRefraction);", "RiverSurface should fall back to the base bottom sample near river banks instead of sampling clear black");
        AssertContains(shader, "float waterDistance = refractionDepth / max(toCameraDir.y, 0.0001f);", "RiverSurface should use CK3's camera-ray water distance for underwater see-through attenuation");
        AssertContains(shader, "ApplyTerrainUnderwaterSeeThrough(refractionDepth, refractionWorldPosition, refractionWaterColorMap, refraction.rgb)", "RiverSurface should use true refraction depth for see-through color, not the shore-fade-clamped depth");
        AssertContains(shader, "float3 waterDiffuse = lerp(WaterColorDeep.rgb, WaterColorShallow.rgb, facing) * _WaterDiffuseMultiplier;", "RiverSurface should derive primary water diffuse from angle-facing shallow/deep colors");
        AssertNotContains(shader, "WaterColorTexture.Sample(WaterTextureSampler, float2(depthFactor, 0.5f))", "RiverSurface should not sample CK3 water-color as a depth ramp");
        AssertNotContains(shader, "waterColor = lerp(waterColor, waterColorSample.rgb, 0.65f);", "RiverSurface should not strongly blend toward the dark CK3 water color texture");
        AssertNotContains(shader, "waterColor = lerp(waterColor, refractedColor, 0.72f);", "RiverSurface should not strongly blend toward the dark bottom/refraction buffer");
    }

    private static void RenderFeatureSeparatesSceneSeedFromWorkingBuffer()
    {
        string resources = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderResources.cs");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");
        string surface = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");

        AssertContains(resources, "public Texture? SceneSeedColor { get; private set; }", "RiverRenderResources should allocate a dedicated scene seed buffer");
        AssertContains(feature, "renderResources.SceneSeedColor", "RiverRenderFeature should use the dedicated scene seed texture");
        AssertContains(feature, "refractionSeedScaler.SetOutput(renderResources.SceneSeedColor);", "RiverRenderFeature should seed scene color into SceneSeedColor first");
        AssertContains(feature, "commandList.CopyRegion(renderResources.SceneSeedColor, 0, null, renderResources.BottomColor, 0);", "RiverRenderFeature should copy the scene seed into the working bottom/refraction buffer before the bottom pass");
        AssertContains(feature, "surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.PointClamp);", "RiverRenderFeature should sample refraction with PointClamp to match CK3 bank behavior");
        AssertContains(surface, "edgeFade1", "RiverSurface should expose the first explicit bank fade term");
        AssertContains(surface, "edgeFade2", "RiverSurface should expose the second explicit bank fade term");
        AssertNotContains(feature, "RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp", "RiverRenderFeature should not keep the previous LinearClamp refraction sampler binding");
    }

    private static void BottomShaderPacksRefractionDistanceForSurfaceSeeThrough()
    {
        string common = ReadRepositoryText("Terrain.Editor/Effects/RiverCommon.sdsl");
        string bottom = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");
        string surface = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");

        AssertContains(common, "float RiverCompressWorldSpace(float3 worldPosition, float3 cameraPosition)", "RiverCommon should pack camera-relative distance like CK3 CompressWorldSpace");
        AssertContains(common, "float3 RiverDecompressWorldSpace(float3 surfaceWorldPosition, float compressedDistance, float3 cameraPosition)", "RiverCommon should provide the matching surface-side decompression");
        AssertContains(bottom, "stage float3 _CameraWorldPosition = float3(0.0f, 0.0f, 0.0f);", "RiverBottom should declare an explicit camera position parameter instead of relying on material-only Eye globals");
        AssertContains(surface, "stage float3 _CameraWorldPosition = float3(0.0f, 0.0f, 0.0f);", "RiverSurface should declare an explicit camera position parameter instead of relying on material-only Eye globals");
        AssertContains(bottom, "float3 bottomWorldPosition = streams.PositionWS.xyz;", "RiverBottom should build the submerged bottom world position");
        AssertContains(bottom, "float compressedWorld = RiverCompressWorldSpace(bottomWorldPosition, _CameraWorldPosition);", "RiverBottom should pack bottom position distance into refraction alpha using the render feature supplied camera position");
        AssertContains(surface, "float3 baseWorldPosition = RiverDecompressWorldSpace(surfaceWorldPosition, baseRefraction.a, _CameraWorldPosition);", "RiverSurface should recover the undistorted bottom world position from refraction alpha using the render feature supplied camera position");
        AssertContains(surface, "float3 distortedWorldPosition = RiverDecompressWorldSpace(surfaceWorldPosition, distortedRefraction.a, _CameraWorldPosition);", "RiverSurface should recover the distorted bottom world position before accepting the offset sample");
        AssertContains(surface, "float3 toCameraDir = normalize(_CameraWorldPosition - streams.PositionWS.xyz);", "RiverSurface should compute facing from the explicit camera position");
        AssertNotContains(bottom, "Eye.xyz", "RiverBottom should not use Eye because this DynamicEffect shader does not import that material global");
        AssertNotContains(surface, "Eye.xyz", "RiverSurface should not use Eye because this DynamicEffect shader does not import that material global");
    }

    private static void ResourceLoaderDoesNotSilentlyIgnoreMissingTextures()
    {
        string loader = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverResourceLoader.cs");

        AssertNotContains(loader, "catch\r\n        {\r\n            return null;\r\n        }", "RiverResourceLoader should not silently return null when texture assets fail to load");
        AssertNotContains(loader, "catch\n        {\n            return null;\n        }", "RiverResourceLoader should not silently return null when texture assets fail to load");
    }

    private static string ReadRepositoryText(string relativePath)
    {
        return File.ReadAllText(GetRepositoryPath(relativePath));
    }

    private static string GetRepositoryPath(string relativePath)
    {
        return Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void AssertContains(string source, string expected, string message)
    {
        TestHarness.Assert(source.Contains(expected, StringComparison.Ordinal), message);
    }

    private static void AssertNotContains(string source, string unexpected, string message)
    {
        TestHarness.Assert(!source.Contains(unexpected, StringComparison.Ordinal), message);
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
}
