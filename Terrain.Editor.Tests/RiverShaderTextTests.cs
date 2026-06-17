namespace Terrain.Editor.Tests;

internal static class RiverShaderTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("river ck3 texture assets have correct Stride color-space flags", Ck3TextureAssetsHaveStrideDescriptors);
        TestHarness.Run("river ck3 texture assets are bundle roots for dynamic loading", Ck3TextureAssetsAreBundleRoots);
        TestHarness.Run("river reflection specular asset remains a cubemap", ReflectionSpecularAssetRemainsCubemap);
        TestHarness.Run("river bottom shader samples texture assets", BottomShaderSamplesTextureAssets);
        TestHarness.Run("river common shader uses ck3 river depth profile", CommonShaderUsesCk3RiverDepthProfile);
        TestHarness.Run("river bottom shader matches ck3 capture world-uv semantics", BottomShaderMatchesCk3CaptureWorldUvSemantics);
        TestHarness.Run("river bottom shader matches ck3 capture alpha semantics", BottomShaderMatchesCk3CaptureAlphaSemantics);
        TestHarness.Run("river bottom shader uses ck3-style material lighting", BottomShaderUsesCk3StyleMaterialLighting);
        TestHarness.Run("river bottom shader applies renderdoc-validated lighting energy boost", BottomShaderAppliesRenderDocValidatedLightingEnergyBoost);
        TestHarness.Run("river render objects carry settings into shader binding", RenderObjectCarriesRiverSettingsToShaderBinding);
        TestHarness.Run("river bottom lighting inputs come from scene bindings", BottomLightingInputsComeFromSceneBindings);
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

    private static void ReflectionSpecularAssetRemainsCubemap()
    {
        string assetPath = GetRepositoryPath("Terrain.Editor/Assets/River/Environment/reflection-specular.dds");
        using var stream = File.OpenRead(assetPath);
        using var reader = new BinaryReader(stream);

        TestHarness.AssertEqual("DDS ", new string(reader.ReadChars(4)), "Reflection cubemap asset should keep the DDS file signature");
        stream.Position = 112;
        uint caps2 = reader.ReadUInt32();
        TestHarness.Assert((caps2 & 0x00000200u) != 0, "Reflection cubemap asset should keep the DDS cubemap capability flag");
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
    }

    private static void CommonShaderUsesCk3RiverDepthProfile()
    {
        string common = ReadRepositoryText("Terrain.Editor/Effects/RiverCommon.sdsl");

        AssertContains(common, "cos(crossSection * 2.0f * 3.14159265f) * 0.5f + 0.5f", "River depth should use CK3's cosine-shaped cross-section profile");
        AssertContains(common, "1.0f - pow(saturate(cosine), max(depthWidthPower, 0.0001f))", "River depth should preserve CK3's cosine profile through the depth-width power");
        AssertNotContains(common, "abs(crossSection * 2.0f - 1.0f)", "River depth should not regress to the older parabolic ribbon profile");
    }

    private static void BottomShaderMatchesCk3CaptureWorldUvSemantics()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "stage float _TextureUvScale", "RiverBottom should expose CK3 texture-UV scaling");
        AssertContains(shader, "stage float _OceanFadeRate", "RiverBottom should expose CK3 ocean fade shaping");
        AssertContains(shader, "stage float _BankAmount", "RiverBottom should expose CK3 bank amount shaping");
        AssertContains(shader, "CalcBottomProfileDepth(float2 tangentUv)", "RiverBottom should compute capture-aligned river depth from tangent-space ribbon UVs");
        AssertContains(shader, "void CalculateParallaxOffsetSteep(", "RiverBottom should implement CK3-style steep parallax stepping");
        AssertContains(shader, "void CalcParallaxedBottomUvs(", "RiverBottom should derive tangent/world UVs through a dedicated parallax helper");
        AssertContains(shader, "float2 scaledRiverUv = float2(riverUv.x * _TextureUvScale, riverUv.y);", "RiverBottom should scale river ribbon UVs with _TextureUvScale before parallax");
        AssertContains(shader, "BottomDiffuseTexture.Sample(BottomTextureSampler, worldUv)", "RiverBottom should sample bottom diffuse in world UV space like the CK3 capture");
        AssertContains(shader, "BottomNormalTexture.Sample(BottomTextureSampler, worldUv)", "RiverBottom should sample bottom normal in world UV space like the CK3 capture");
        AssertContains(shader, "BottomPropertiesTexture.Sample(BottomTextureSampler, worldUv)", "RiverBottom should sample bottom properties in world UV space like the CK3 capture");
        AssertContains(shader, "float depth = CalcBottomProfileDepth(tangentUv);", "RiverBottom should keep depth shaping on tangent UV while using world UV for the material samples");
        AssertContains(shader, "float safeDenom = abs(denom) > 0.0001f ? denom : (denom >= 0.0f ? 0.0001f : -0.0001f);", "RiverBottom should preserve the parallax interpolation denominator sign before clamping");
        AssertContains(shader, "float weight = saturate(nextDepth / safeDenom);", "RiverBottom should clamp steep parallax interpolation to the current segment instead of extrapolating");
        AssertNotContains(shader, "CalcBottomDepth(float2 tangentUv, float4 bottomNormalSample)", "RiverBottom should no longer lock capture parity to the advanced bottom-normal depth path");
        AssertNotContains(shader, "BottomDiffuseTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom should no longer sample diffuse in tangent UV space for the CK3 capture-aligned path");
        AssertNotContains(shader, "BottomNormalTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom should no longer sample normals in tangent UV space for the CK3 capture-aligned path");
        AssertNotContains(shader, "BottomPropertiesTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom should no longer sample properties in tangent UV space for the CK3 capture-aligned path");
        AssertNotContains(shader, "float3x3(tangent, bitangent, surfaceNormal)", "RiverBottom should use a scalar TBN matrix constructor compatible with D3D shader compilation");
        AssertNotContains(shader, "float2 ComputeBottomWorldUv(float3 worldPosition)", "RiverBottom should no longer centralize bottom sampling around world-space UVs");
    }

    private static void BottomShaderMatchesCk3CaptureAlphaSemantics()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "float underOceanFade = 1.0f - saturate(max(0.0f, -streams.PositionWS.y) * _OceanFadeRate);", "RiverBottom should compute CK3-style underwater fade before bottom alpha");
        AssertContains(shader, "float fadeOut = min(underOceanFade, saturate(streams.RiverTransparency));", "RiverBottom should use a dedicated fadeOut term for bottom blending");
        AssertContains(shader, "float edgeFade = saturate(depth * 13.0f);", "RiverBottom should use the CK3 capture depth-driven edge fade");
        AssertContains(shader, "float alpha = fadeOut * connectionFade * edgeFade;", "RiverBottom alpha should match the CK3 capture path instead of the advanced diffuse-alpha fade");
        AssertNotContains(shader, "float edgeFade = max(_BankFade, 0.0001f);", "RiverBottom should not keep the advanced smoothstep bank fade when aligning to the CK3 capture");
        AssertNotContains(shader, "smoothstep(0.0f, edgeFade, riverUv.y)", "RiverBottom should not keep the advanced per-bank smoothstep alpha for the CK3 capture path");
        AssertNotContains(shader, "float alpha = bottomDiffuse.a * fadeOut * connectionFade * edgeFade1 * edgeFade2;", "RiverBottom alpha should no longer be driven by diffuse alpha when matching the CK3 capture path");
    }

    private static void BottomShaderUsesCk3StyleMaterialLighting()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");

        AssertContains(shader, "TextureCube<float4> EnvironmentMapTexture", "RiverBottom should bind a cube environment map like CK3 bottom lighting");
        AssertContains(shader, "EnvironmentMapTexture.SampleLevel", "RiverBottom should sample environment IBL instead of baking brightness into diffuse");
        AssertContains(shader, "CalculateRiverBottomLighting", "RiverBottom should route bottom albedo through a material lighting function");
        AssertContains(shader, "_SceneSunDirection", "RiverBottom should bind the visible scene directional-light direction");
        AssertContains(shader, "_SceneSunColor", "RiverBottom should bind the visible scene directional-light color");
        AssertContains(shader, "_SceneShadowCascadeCount", "RiverBottom should bind the real scene shadow cascade count");
        AssertContains(shader, "_SceneShadowCascadeSplits", "RiverBottom should bind the real scene shadow cascade splits");
        AssertContains(shader, "_SceneWorldToShadowCascadeUV", "RiverBottom should bind the real scene world-to-shadow matrices");
        AssertContains(shader, "_SceneShadowDepthBias", "RiverBottom should bind the real scene shadow depth bias");
        AssertContains(shader, "_SceneShadowOffsetScale", "RiverBottom should bind the real scene shadow normal-offset scale");
        AssertContains(shader, "SceneShadowMapTexture.SampleCmpLevelZero", "RiverBottom should sample the scene shadow map instead of using a river-local shadow fallback");
        AssertContains(shader, "FilterSceneShadow5x5", "RiverBottom should apply the scene PCF filter to bottom shadows");
        AssertContains(shader, "_EnvironmentSkyMatrix", "RiverBottom should rotate environment lighting through the scene skybox matrix");
        AssertContains(shader, "_EnvironmentIntensity", "RiverBottom should scale environment lighting from the scene skybox intensity");
        AssertContains(shader, "_EnvironmentMipCount", "RiverBottom should derive diffuse/specular IBL mips from the bound scene cubemap");
        AssertContains(shader, "RotateEnvironmentSampleDirection", "RiverBottom should centralize scene skybox rotation for IBL sampling");
        AssertContains(shader, "RiverUnpackRRxGNormal", "RiverBottom should use CK3's RRxG normal packing for bottom normals");
        AssertContains(shader, "dot(viewDir, tangent)", "RiverBottom should transform the view direction through the river tangent basis");
        AssertContains(shader, "parallaxNormal.x * tangent + parallaxNormal.y * bitangent + parallaxNormal.z * surfaceNormal", "RiverBottom should transform the sampled bottom normal through the river tangent basis");
        AssertContains(feature, "RiverBottomKeys.EnvironmentMapTexture", "RiverRenderFeature should bind the environment cubemap to the bottom pass");
        AssertNotContains(shader, "stage compose DirectLightGroup bottomLightGroup;", "RiverBottom should no longer depend on the brittle DirectLightGroup permutation path");
        AssertNotContains(shader, "bottomLightGroup.PrepareDirectLight(0);", "RiverBottom should not depend on a composed light group for scene shadows anymore");
        AssertNotContains(shader, "_BottomSunDirection", "RiverBottom should not keep a river-local sun direction once bottom lighting is scene-driven");
        AssertNotContains(shader, "_BottomSunColor", "RiverBottom should not keep a river-local sun color once bottom lighting is scene-driven");
        AssertNotContains(shader, "_BottomSunIntensity", "RiverBottom should not keep a river-local sun intensity once bottom lighting is scene-driven");
        AssertNotContains(shader, "_ShadowTermFallback", "RiverBottom should not keep the old neutral-lighting shadow fallback once bottom lighting is scene-driven");
        AssertNotContains(shader, "_CloudMaskFallback", "RiverBottom should not keep the old neutral-lighting cloud fallback once bottom lighting is scene-driven");
        AssertNotContains(shader, "_BottomDiffuseMultiplier", "RiverBottom should not use a brightness multiplier to hide missing lighting");
        AssertNotContains(shader, "bottomDiffuse.rgb * depthTint * 2.0f", "RiverBottom should not darken albedo by depth tint as a substitute for CK3 material lighting");
    }

    private static void BottomShaderAppliesRenderDocValidatedLightingEnergyBoost()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(
            shader,
            "CalculateRiverBottomLighting(streams.PositionWS.xyz, bottomDiffuse.rgb, bottomNormal, viewDir, bottomProperties) * 3.0f",
            "RiverBottom should apply the RenderDoc-validated 3x lighting gain to the fully lit bottom color");
    }

    private static void RenderObjectCarriesRiverSettingsToShaderBinding()
    {
        string renderObject = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderObject.cs");
        string settings = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderSettings.cs");
        string processor = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverProcessor.cs");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");

        AssertContains(settings, "public float BottomNormalStrength { get; set; } = 1.0f;", "RiverRenderSettings should expose bottom normal strength for bottom lighting");
        AssertContains(settings, "public float BottomEnvironmentIntensity { get; set; }", "RiverRenderSettings should expose bottom environment intensity");
        AssertContains(settings, "public float BottomSpecularIntensity { get; set; }", "RiverRenderSettings should expose bottom specular intensity");
        AssertNotContains(settings, "public Vector3 BottomSunDirection { get; set; }", "RiverRenderSettings should no longer expose a river-local bottom sun direction once bottom lighting is scene-driven");
        AssertNotContains(settings, "public Vector3 BottomSunColor { get; set; }", "RiverRenderSettings should no longer expose a river-local bottom sun color once bottom lighting is scene-driven");
        AssertNotContains(settings, "public float BottomSunIntensity { get; set; }", "RiverRenderSettings should no longer expose a river-local bottom sun intensity once bottom lighting is scene-driven");
        AssertContains(renderObject, "public float TextureUvScale { get; private set; } = 1.0f;", "RiverRenderObject should cache texture UV scale for shader binding");
        AssertContains(renderObject, "public Vector2 MapWorldSize { get; private set; } = new(4096.0f, 4096.0f);", "RiverRenderObject should cache per-axis map world size for rectangular map UV normalization");
        AssertContains(renderObject, "public float OceanFadeRate { get; private set; } = 0.8f;", "RiverRenderObject should cache ocean fade rate for shader binding");
        AssertContains(renderObject, "public float BankAmount { get; private set; } = 0.0f;", "RiverRenderObject should cache bank amount for shader binding");
        AssertContains(renderObject, "public int ParallaxIterations { get; private set; } = 10;", "RiverRenderObject should cache parallax iteration count for shader binding");
        AssertContains(renderObject, "public float BottomNormalStrength { get; private set; } = 1.0f;", "RiverRenderObject should cache bottom normal strength for shader binding");
        AssertContains(renderObject, "public float BottomEnvironmentIntensity { get; private set; }", "RiverRenderObject should cache bottom environment intensity");
        AssertContains(renderObject, "public float BottomSpecularIntensity { get; private set; }", "RiverRenderObject should cache bottom specular intensity");
        AssertNotContains(renderObject, "public Vector3 BottomSunDirection { get; private set; }", "RiverRenderObject should not cache a river-local bottom sun direction once bottom lighting is scene-driven");
        AssertNotContains(renderObject, "public Vector3 BottomSunColor { get; private set; }", "RiverRenderObject should not cache a river-local bottom sun color once bottom lighting is scene-driven");
        AssertNotContains(renderObject, "public float BottomSunIntensity { get; private set; }", "RiverRenderObject should not cache a river-local bottom sun intensity once bottom lighting is scene-driven");
        AssertContains(renderObject, "public void ApplySettings(RiverRenderSettings settings)", "RiverRenderObject should snapshot river settings from the component");
        AssertContains(renderObject, "TextureUvScale = settings.TextureUvScale;", "RiverRenderObject should copy texture UV scale from RiverRenderSettings");
        AssertContains(renderObject, "OceanFadeRate = settings.OceanFadeRate;", "RiverRenderObject should copy ocean fade rate from RiverRenderSettings");
        AssertContains(renderObject, "BankAmount = settings.BankAmount;", "RiverRenderObject should copy bank amount from RiverRenderSettings");
        AssertContains(renderObject, "ParallaxIterations = settings.ParallaxIterations;", "RiverRenderObject should copy parallax iteration count from RiverRenderSettings");
        AssertContains(renderObject, "BottomNormalStrength = settings.BottomNormalStrength;", "RiverRenderObject should copy bottom normal strength from RiverRenderSettings");
        AssertContains(renderObject, "BottomEnvironmentIntensity = settings.BottomEnvironmentIntensity;", "RiverRenderObject should copy bottom environment intensity from RiverRenderSettings");
        AssertContains(renderObject, "BottomSpecularIntensity = settings.BottomSpecularIntensity;", "RiverRenderObject should copy bottom specular intensity from RiverRenderSettings");
        AssertNotContains(renderObject, "BottomSunDirection = settings.BottomSunDirection;", "RiverRenderObject should not copy a river-local bottom sun direction from RiverRenderSettings");
        AssertNotContains(renderObject, "BottomSunColor = settings.BottomSunColor;", "RiverRenderObject should not copy a river-local bottom sun color from RiverRenderSettings");
        AssertNotContains(renderObject, "BottomSunIntensity = settings.BottomSunIntensity;", "RiverRenderObject should not copy a river-local bottom sun intensity from RiverRenderSettings");
        AssertContains(renderObject, "MapWorldSize = mesh.MapWorldSize;", "RiverRenderObject should keep the per-axis map world size from the generated river mesh");
        AssertContains(processor, "renderObject.ApplySettings(component.Settings);", "RiverProcessor should push component settings into each render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._TextureUvScale, riverObject.TextureUvScale);", "RiverRenderFeature should bind bottom texture UV scale from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._BankAmount, riverObject.BankAmount);", "RiverRenderFeature should bind bottom bank amount from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._OceanFadeRate, riverObject.OceanFadeRate);", "RiverRenderFeature should bind bottom ocean fade rate from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._ParallaxIterations, riverObject.ParallaxIterations);", "RiverRenderFeature should bind bottom parallax iteration count from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._BottomNormalStrength, riverObject.BottomNormalStrength);", "RiverRenderFeature should bind bottom normal strength from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._BottomEnvironmentIntensity, riverObject.BottomEnvironmentIntensity);", "RiverRenderFeature should bind bottom environment intensity from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._BottomSpecularIntensity, riverObject.BottomSpecularIntensity);", "RiverRenderFeature should bind bottom specular intensity from the render object");
        AssertNotContains(feature, "effect.Parameters.Set(RiverBottomKeys._BottomSunDirection, riverObject.BottomSunDirection);", "RiverRenderFeature should not bind a river-local bottom sun direction once bottom lighting is scene-driven");
        AssertNotContains(feature, "effect.Parameters.Set(RiverBottomKeys._BottomSunColor, riverObject.BottomSunColor);", "RiverRenderFeature should not bind a river-local bottom sun color once bottom lighting is scene-driven");
        AssertNotContains(feature, "effect.Parameters.Set(RiverBottomKeys._BottomSunIntensity, riverObject.BottomSunIntensity);", "RiverRenderFeature should not bind a river-local bottom sun intensity once bottom lighting is scene-driven");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._MapWorldSize, riverObject.MapWorldSize);", "RiverRenderFeature should bind per-axis map world size into the surface shader");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._FlowNormalUvScale, riverObject.FlowNormalUvScale);", "RiverRenderFeature should bind surface flow-normal UV scale from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys.WaterColorShallow, riverObject.WaterColorShallow);", "RiverRenderFeature should bind shallow water color from the render object");
    }

    private static void BottomLightingInputsComeFromSceneBindings()
    {
        string loader = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverResourceLoader.cs");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");
        string viewportGame = ReadRepositoryText("Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs");

        AssertContains(loader, "public const string BottomEnvironmentUrl = \"Skybox texture\";", "RiverResourceLoader should keep a scene-skybox texture fallback for bottom environment binding");
        AssertContains(loader, "public Texture? BottomEnvironment { get; private set; }", "RiverResourceLoader should expose a dedicated bottom environment cubemap");
        AssertContains(loader, "public Texture? EnsureBottomEnvironment(ContentManager content)", "RiverResourceLoader should lazy-load the fallback skybox cubemap only when the scene skybox is missing");
        AssertContains(loader, "private bool bottomEnvironmentLoadAttempted;", "RiverResourceLoader should memoize whether the optional bottom environment load has already been attempted");
        AssertContains(loader, "if (bottomEnvironmentLoadAttempted)", "RiverResourceLoader should skip repeated optional fallback loads after the first attempt");
        AssertContains(loader, "bottomEnvironmentLoadAttempted = true;", "RiverResourceLoader should record optional fallback load attempts even when the load fails");
        AssertContains(loader, "BottomEnvironment = LoadOptionalTexture(content, BottomEnvironmentUrl);", "RiverResourceLoader should attempt the legacy bottom environment cubemap load at most once per loader lifetime");
        AssertNotContains(loader, "BottomEnvironment = LoadRequiredTexture(content, BottomEnvironmentUrl);", "RiverResourceLoader should not force-load the legacy bottom environment cubemap during feature startup");
        AssertContains(feature, "bottomShadowMapRenderer = forwardLightingFeature?.ShadowMapRenderer;", "RiverRenderFeature should grab Stride's shadow-map renderer from forward lighting");
        AssertContains(feature, "var lightingView = renderView.LightingView ?? renderView;", "RiverRenderFeature should resolve bottom lighting from the current lighting view");
        AssertContains(feature, "var renderViewLightData = TryGetRenderViewLightData(lightingView);", "RiverRenderFeature should ask forward lighting for the current view's light data before selecting sun and skybox");
        AssertContains(feature, "var lights = renderViewLightData?.VisibleLights ?? CollectFallbackVisibleLights(lightingView);", "RiverRenderFeature should prefer forward lighting's per-view visible-light cache when available");
        AssertContains(feature, "SelectBottomDirectionalLight(lights, renderViewLightData, lightingView)", "RiverRenderFeature should choose the bottom directional light through a dedicated selection helper");
        AssertContains(feature, "renderViewLightData.VisibleLightsWithShadows", "RiverRenderFeature should prefer visible directional lights that actually have shadows");
        AssertContains(feature, "renderViewLightData?.RenderLightsWithShadows.TryGetValue(directionalLight, out var shadowMapTexture)", "RiverRenderFeature should reuse forward lighting's shadow-map lookup when available");
        AssertContains(feature, "SelectBottomSkyboxLight(lights)", "RiverRenderFeature should choose the bottom skybox through a dedicated selection helper");
        AssertContains(feature, "TryGetSceneEnvironmentTexture(light) != null", "RiverRenderFeature should prefer visible skyboxes that already expose a real scene cubemap");
        AssertContains(feature, "LightDirectionalShadowMapRenderer.ShaderData", "RiverRenderFeature should read Stride's directional shadow shader data for the bottom pass");
        AssertContains(feature, "RiverBottomKeys._SceneSunDirection", "RiverRenderFeature should bind the scene directional-light direction for the bottom pass");
        AssertContains(feature, "RiverBottomKeys._SceneSunColor", "RiverRenderFeature should bind the scene directional-light color for the bottom pass");
        AssertContains(feature, "RiverBottomKeys._SceneShadowCascadeCount", "RiverRenderFeature should bind the real scene shadow cascade count");
        AssertContains(feature, "RiverBottomKeys._SceneShadowCascadeSplits", "RiverRenderFeature should bind the real scene shadow cascade splits");
        AssertContains(feature, "RiverBottomKeys._SceneWorldToShadowCascadeUV", "RiverRenderFeature should bind the real scene world-to-shadow matrices");
        AssertContains(feature, "RiverBottomKeys._SceneShadowDepthBias", "RiverRenderFeature should bind the real scene shadow depth bias");
        AssertContains(feature, "RiverBottomKeys._SceneShadowOffsetScale", "RiverRenderFeature should bind the real scene shadow offset scale");
        AssertContains(feature, "RiverBottomKeys._SceneShadowMapTextureSize", "RiverRenderFeature should bind the scene shadow atlas size");
        AssertContains(feature, "RiverBottomKeys._SceneShadowMapTextureTexelSize", "RiverRenderFeature should bind the scene shadow atlas texel size");
        AssertContains(feature, "RiverBottomKeys.SceneShadowMapTexture", "RiverRenderFeature should bind the real scene shadow atlas texture");
        AssertContains(feature, "TryGetSceneEnvironmentTexture(skyboxLight)", "RiverRenderFeature should prefer the scene LightSkybox cubemap over river-local fallback textures");
        AssertContains(feature, "riverResources.EnsureBottomEnvironment(contentManager)", "RiverRenderFeature should only ask the loader for the legacy cubemap fallback when the scene skybox is absent");
        AssertContains(feature, "bottomEnvironment ??= riverResources.ReflectionSpecular;", "RiverRenderFeature should keep reflection-specular as the final non-null cubemap fallback when both scene skybox and legacy skybox texture are unavailable");
        AssertContains(feature, "SkyboxKeys.CubeMap", "RiverRenderFeature should read the real scene skybox specular cubemap");
        AssertContains(feature, "RiverBottomKeys._EnvironmentSkyMatrix", "RiverRenderFeature should bind scene skybox rotation for bottom IBL");
        AssertContains(feature, "RiverBottomKeys._EnvironmentIntensity", "RiverRenderFeature should bind scene skybox intensity for bottom IBL");
        AssertContains(feature, "RiverBottomKeys._EnvironmentMipCount", "RiverRenderFeature should bind scene cubemap mip count for bottom IBL");
        AssertContains(feature, "SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.ReflectionSpecularTexture, riverResources.ReflectionSpecular);", "RiverRenderFeature should keep the river reflection/specular asset on the surface pass");
        AssertContains(feature, "surfaceEffect.Parameters.Set(RiverSurfaceKeys.ReflectionSpecularSampler, graphicsDevice.SamplerStates.LinearClamp);", "RiverRenderFeature should bind the cubemap sampler for surface reflections explicitly");
        AssertContains(viewportGame, "_riverComponent.Settings.BottomEnvironmentIntensity = 1.0f;", "EmbeddedStrideViewportGame should keep only bottom environment tuning as an explicit river-side multiplier");
        AssertNotContains(feature, "RiverBottomEffectKeys.BottomLightGroup", "RiverRenderFeature should not depend on a RiverBottomEffect light-group permutation anymore");
        AssertNotContains(feature, "CreateLightShaderGroup", "RiverRenderFeature should not build a composed light group for the bottom pass anymore");
        AssertNotContains(feature, "RiverBottomKeys._ShadowTermFallback", "RiverRenderFeature should not bind the old river-local shadow fallback into the bottom pass anymore");
        AssertNotContains(feature, "RiverBottomKeys._CloudMaskFallback", "RiverRenderFeature should not bind the old river-local cloud-mask fallback into the bottom pass anymore");
        AssertNotContains(viewportGame, "_riverComponent.Settings.BottomSunIntensity =", "EmbeddedStrideViewportGame should no longer override the scene directional-light intensity for river bottom");
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
        AssertContains(shader, "TextureCube<float4> ReflectionSpecularTexture", "RiverSurface should treat reflection-specular as a cubemap, matching the imported DDS asset");
        AssertContains(shader, "ReflectionSpecularSampler", "RiverSurface should use an explicit cubemap sampler for reflections");
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
        AssertContains(shader, "worldPosition.xz / max(_MapWorldSize, float2(1.0f, 1.0f))", "RiverSurface should normalize map UVs per axis instead of collapsing rectangular maps to a single scalar extent");
        AssertContains(shader, "uv.y = 1.0f - uv.y;", "RiverSurface should flip map-space Y like CK3 before sampling water-color maps");
        AssertContains(shader, "float2 worldUv = ComputeMapWorldUv(streams.PositionWS.xyz);", "RiverSurface should sample surface water color/spec in CK3 map world UV space");
        AssertContains(shader, "float4 waterColorAndSpec = WaterColorTexture.Sample(WaterTextureSampler, worldUv);", "RiverSurface should sample CK3 water color/spec texture in map world UV space");
        AssertContains(shader, "float glossMap = waterColorAndSpec.a;", "RiverSurface should use CK3 water-color alpha as the gloss/spec map");
        AssertContains(shader, "float3 DecodeRefractionWorldPosition(float3 surfaceWorldPosition, float compressedDistance)", "RiverSurface should centralize refraction-payload decode so invalid seed pixels do not decompress to camera space");
        AssertContains(shader, "float2 refractionWorldUv = ComputeMapWorldUv(refractionWorldPosition);", "RiverSurface should sample refraction tint at the refracted bottom world position like CK3");
        AssertContains(shader, "float4 refractionWaterColorAndSpec = WaterColorTexture.Sample(WaterTextureSampler, refractionWorldUv);", "RiverSurface should resample CK3 water-color at the accepted refraction world position");
        AssertContains(shader, "float3 refractionWaterColorMap = lerp(refractionWaterColorAndSpec.rgb, _WaterColorMapTint, saturate(_WaterColorMapTintFactor));", "RiverSurface should treat refraction water-color RGB as the see-through tint map");
        AssertContains(shader, "float4 refractionResult = SampleRefractionSeeThrough(screenUv, refractionOffset, streams.PositionWS.xyz, worldDepth);", "RiverSurface should compute CK3-style see-through refraction from the bottom buffer");
        AssertContains(shader, "float3 refractionColor = refractionResult.rgb;", "RiverSurface should keep refraction color separate from the depth used for shore fade");
        AssertContains(shader, "float refractionFadeDepth = refractionResult.a;", "RiverSurface should use the same refraction depth as CK3 for water fade");
        AssertContains(shader, "float4 baseRefraction = RefractionTexture.Sample(RefractionSampler, saturate(screenUv));", "RiverSurface should first sample the undistorted bottom buffer like CK3");
        AssertContains(shader, "float4 distortedRefraction = RefractionTexture.Sample(RefractionSampler, saturate(screenUv + refractionOffset));", "RiverSurface should sample distorted refraction separately from the base sample");
        AssertContains(shader, "compressedDistance > 0.0001f", "RiverSurface should treat missing refraction payload alpha as invalid instead of decompressing from zero distance");
        AssertContains(shader, "float3 baseWorldPosition = DecodeRefractionWorldPosition(surfaceWorldPosition, baseRefraction.a);", "RiverSurface should decode the undistorted refraction payload through the invalid-alpha guard");
        AssertContains(shader, "float3 distortedWorldPosition = DecodeRefractionWorldPosition(surfaceWorldPosition, distortedRefraction.a);", "RiverSurface should decode the distorted refraction payload through the invalid-alpha guard");
        AssertContains(shader, "float useDistortedRefraction = (distortedRefraction.a > 0.0001f && distortedWorldPosition.y <= surfaceWorldPosition.y) ? 1.0f : 0.0f;", "RiverSurface should reject distorted refraction samples that land outside the bottom buffer or above the water surface");
        AssertContains(shader, "float4 refraction = lerp(baseRefraction, distortedRefraction, useDistortedRefraction);", "RiverSurface should fall back to the base bottom sample near river banks instead of sampling clear black");
        AssertContains(shader, "float waterDistance = refractionDepth / max(toCameraDir.y, 0.0001f);", "RiverSurface should use CK3's camera-ray water distance for underwater see-through attenuation");
        AssertContains(shader, "ApplyTerrainUnderwaterSeeThrough(effectiveDepth, refractionWorldPosition, refractionWaterColorMap, refraction.rgb)", "RiverSurface should cap see-through attenuation by the shallower of surface depth and refracted bottom depth");
        AssertContains(shader, "float3 waterDiffuse = lerp(WaterColorDeep.rgb, WaterColorShallow.rgb, facing) * _WaterDiffuseMultiplier;", "RiverSurface should derive primary water diffuse from angle-facing shallow/deep colors");
        AssertContains(shader, "float3 reflectionVector = normalize(reflect(-toCameraDir, waterNormal));", "RiverSurface should build a real reflection vector before sampling the cubemap");
        AssertContains(shader, "ReflectionSpecularTexture.Sample(ReflectionSpecularSampler, reflectionVector)", "RiverSurface should sample the reflection cubemap directly instead of treating it as a 2D lookup strip");
        AssertContains(shader, "float edgeFade1 = smoothstep(0.0f, max(_BankFade, 0.0001f), riverUv.y);", "RiverSurface should make the first bank fade explicit");
        AssertContains(shader, "float edgeFade2 = smoothstep(0.0f, max(_BankFade, 0.0001f), 1.0f - riverUv.y);", "RiverSurface should make the second bank fade explicit");
        AssertNotContains(shader, "worldPosition.xz / max(_MapExtent, 1.0f)", "RiverSurface should not collapse map UVs to the max axis extent on rectangular maps");
        AssertNotContains(shader, "ReflectionSpecularTexture.Sample(WaterTextureSampler, worldUv * 0.5f + flowNormal.xz * 0.03f)", "RiverSurface should not treat the reflection cubemap as a 2D map-space variation texture");
        AssertNotContains(shader, "WaterColorTexture.Sample(WaterTextureSampler, float2(depthFactor, 0.5f))", "RiverSurface should not sample CK3 water-color as a depth ramp");
        AssertNotContains(shader, "waterColor = lerp(waterColor, waterColorSample.rgb, 0.65f);", "RiverSurface should not strongly blend toward the dark CK3 water color texture");
        AssertNotContains(shader, "waterColor = lerp(waterColor, refractedColor, 0.72f);", "RiverSurface should not strongly blend toward the dark bottom/refraction buffer");
    }

    private static void RenderFeatureSeparatesSceneSeedFromWorkingBuffer()
    {
        string resources = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderResources.cs");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");

        AssertContains(resources, "public Texture? SceneSeedColor { get; private set; }", "RiverRenderResources should allocate a dedicated scene seed buffer");
        AssertContains(feature, "renderResources.SceneSeedColor", "RiverRenderFeature should use the dedicated scene seed texture");
        AssertContains(feature, "refractionSeedScaler.SetOutput(renderResources.SceneSeedColor);", "RiverRenderFeature should seed scene color into SceneSeedColor first");
        AssertContains(feature, "commandList.CopyRegion(renderResources.SceneSeedColor, 0, null, renderResources.BottomColor, 0);", "RiverRenderFeature should copy the scene seed into the working bottom/refraction buffer before the bottom pass");
        AssertContains(feature, "surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.PointClamp);", "RiverRenderFeature should sample refraction with PointClamp to match CK3 bank behavior");
        AssertContains(feature, "blendState.RenderTargets[0].AlphaSourceBlend = Blend.One;", "RiverRenderFeature should write refraction payload alpha directly instead of blending it by bottom coverage");
        AssertContains(feature, "blendState.RenderTargets[0].AlphaDestinationBlend = Blend.Zero;", "RiverRenderFeature should not preserve the scene-seed alpha under partially covered bottom pixels");
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
        AssertContains(bottom, "float3 bottomWorldPosition;", "RiverBottom should build the submerged bottom world position from a dedicated variable");
        AssertContains(bottom, "bottomWorldPosition.xz = worldUv;", "RiverBottom should place the submerged bottom position at the parallaxed world UV");
        AssertContains(bottom, "bottomWorldPosition.y = streams.PositionWS.y - worldDepth;", "RiverBottom should drop the submerged bottom position by the computed world depth");
        AssertContains(bottom, "float compressedWorld = RiverCompressWorldSpace(bottomWorldPosition, _CameraWorldPosition);", "RiverBottom should pack bottom position distance into refraction alpha using the render feature supplied camera position");
        AssertContains(surface, "float3 baseWorldPosition = DecodeRefractionWorldPosition(surfaceWorldPosition, baseRefraction.a);", "RiverSurface should recover the undistorted bottom world position through the invalid-alpha guard");
        AssertContains(surface, "float3 distortedWorldPosition = DecodeRefractionWorldPosition(surfaceWorldPosition, distortedRefraction.a);", "RiverSurface should recover the distorted bottom world position through the invalid-alpha guard");
        AssertContains(surface, "float3 toCameraDir = normalize(_CameraWorldPosition - streams.PositionWS.xyz);", "RiverSurface should compute facing from the explicit camera position");
        AssertNotContains(bottom, "Eye.xyz", "RiverBottom should not use Eye because this DynamicEffect shader does not import that material global");
        AssertNotContains(surface, "Eye.xyz", "RiverSurface should not use Eye because this DynamicEffect shader does not import that material global");
    }

    private static void ResourceLoaderDoesNotSilentlyIgnoreMissingTextures()
    {
        string loader = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverResourceLoader.cs");

        AssertContains(loader, "catch (Exception exception)", "RiverResourceLoader should explicitly handle load failures instead of silently returning null");
        AssertContains(loader, "throw new InvalidOperationException($\"River texture asset '{url}' could not be loaded. Ensure the .sdtex is included as a RootAsset in Terrain.Editor.sdpkg.\", exception);", "RiverResourceLoader should rethrow load failures with actionable context");
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
