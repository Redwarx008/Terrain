namespace Terrain.Editor.Tests;

internal static class RiverShaderTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("river texture assets have correct Stride color-space flags", RiverTextureAssetsHaveStrideDescriptors);
        TestHarness.Run("river texture assets are bundle roots for dynamic loading", RiverTextureAssetsAreBundleRoots);
        TestHarness.Run("river reflection specular asset remains a cubemap", ReflectionSpecularAssetRemainsCubemap);
        TestHarness.Run("river bottom lighting uses map scene environment inputs", BottomLightingUsesMapSceneEnvironmentInputs);
        TestHarness.Run("river bottom shader samples texture assets", BottomShaderSamplesTextureAssets);
        TestHarness.Run("river common shader uses cosine river depth profile", CommonShaderUsesCosineRiverDepthProfile);
        TestHarness.Run("river bottom shader uses target advanced uv and parallax semantics", BottomShaderUsesTargetAdvancedUvAndParallaxSemantics);
        TestHarness.Run("river bottom shader uses target advanced alpha semantics", BottomShaderUsesTargetAdvancedAlphaSemantics);
        TestHarness.Run("river bottom shader uses target bottom shadow path", BottomShaderUsesTargetBottomShadowPath);
        TestHarness.Run("river bottom shader uses scene material lighting", BottomShaderUsesSceneMaterialLighting);
        TestHarness.Run("river bottom shader does not apply a global lighting energy boost", BottomShaderDoesNotApplyGlobalLightingEnergyBoost);
        TestHarness.Run("river render objects carry settings into shader binding", RenderObjectCarriesRiverSettingsToShaderBinding);
        TestHarness.Run("river bottom lighting inputs come from scene bindings", BottomLightingInputsComeFromSceneBindings);
        TestHarness.Run("river surface shader samples water texture assets", SurfaceShaderSamplesWaterTextureAssets);
        TestHarness.Run("river surface shader uses target water normals and flow", SurfaceShaderUsesTargetWaterNormalsAndFlow);
        TestHarness.Run("river surface shader follows target refraction and fade semantics", SurfaceShaderFollowsTargetRefractionAndFadeSemantics);
        TestHarness.Run("river surface shader uses target water lighting structure", SurfaceShaderUsesTargetWaterLightingStructure);
        TestHarness.Run("river surface shader post step applies target shadow and fog wrapper", SurfaceShaderPostStepAppliesTargetShadowAndFogWrapper);
        TestHarness.Run("river render feature separates scene seed from working refraction buffer", RenderFeatureSeparatesSceneSeedFromWorkingBuffer);
        TestHarness.Run("river scene seed writes depth payload instead of clearing alpha", RenderFeatureSceneSeedWritesDepthPayload);
        TestHarness.Run("river bottom shader packs refraction distance for surface see through", BottomShaderPacksRefractionDistanceForSurfaceSeeThrough);
        TestHarness.Run("river resource loader does not silently ignore missing textures", ResourceLoaderDoesNotSilentlyIgnoreMissingTextures);
    }

    private static void RiverTextureAssetsHaveStrideDescriptors()
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
            ("Terrain.Editor/Assets/River/Water/shadow-color.sdtex", "shadow-color.dds", true),
            ("Terrain.Editor/Assets/River/Water/water-color.sdtex", "water-color.dds", false),
            ("Terrain.Editor/Assets/River/Environment/reflection-specular.sdtex", "reflection-specular.dds", false),
        ];

        foreach (var (descriptorPath, sourcePath, useSRgbSampling) in assets)
        {
            string fullDescriptorPath = GetRepositoryPath(descriptorPath);
            TestHarness.Assert(File.Exists(fullDescriptorPath), $"{descriptorPath} should exist so Content.Load can resolve river texture assets");

            string descriptor = File.ReadAllText(fullDescriptorPath);
            AssertContains(descriptor, "!Texture", $"{descriptorPath} should be a Stride texture asset");
            AssertContains(descriptor, sourcePath, $"{descriptorPath} should point at {sourcePath}");
            AssertContains(descriptor, "!ColorTextureType", $"{descriptorPath} should import packed DDS channels without normal-map conversion");
            AssertContains(
                descriptor,
                $"UseSRgbSampling: {useSRgbSampling.ToString().ToLowerInvariant()}",
                $"{descriptorPath} should preserve the approved color-space sampling mode");

            string fullSourcePath = Path.Combine(Path.GetDirectoryName(fullDescriptorPath)!, sourcePath);
            TestHarness.Assert(File.Exists(fullSourcePath), $"{sourcePath} should exist next to {descriptorPath}");

            if (descriptorPath.EndsWith("water-color.sdtex", StringComparison.Ordinal))
            {
                AssertContains(descriptor, "IsCompressed: false", "water-color should avoid Stride BC3 recompression and preserve source color bytes");
            }
        }
    }

    private static void RiverTextureAssetsAreBundleRoots()
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
            "River/Water/shadow-color",
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

    private static void BottomLightingUsesMapSceneEnvironmentInputs()
    {
        string descriptorPath = GetRepositoryPath("Terrain.Editor/Assets/Scene/Environment/jomini-environment-terrain-sunny.sdtex");
        TestHarness.Assert(File.Exists(descriptorPath), "Terrain environment should be imported as a scene texture asset");

        string descriptor = File.ReadAllText(descriptorPath);
        AssertContains(descriptor, "jomini-environment-terrain-sunny.dds", "Scene environment descriptor should point at the terrain sunny environment DDS");
        AssertContains(descriptor, "UseSRgbSampling: true", "Terrain environment should keep sRGB cubemap sampling");

        string sourcePath = Path.Combine(Path.GetDirectoryName(descriptorPath)!, "jomini-environment-terrain-sunny.dds");
        TestHarness.Assert(File.Exists(sourcePath), "Terrain environment source DDS should exist next to its descriptor");

        string package = ReadRepositoryText("Terrain.Editor/Terrain.Editor.sdpkg");
        AssertContains(package, ":Scene/Environment/jomini-environment-terrain-sunny", "Terrain environment should be a RootAsset for direct scene loading");

        string viewportGame = ReadRepositoryText("Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs");
        AssertContains(viewportGame, "MapSceneEnvironmentUrl = \"Scene/Environment/jomini-environment-terrain-sunny\"", "Editor scene should load the terrain environment as a scene-level texture");
        AssertContains(viewportGame, "MapSunDiffuseLinear = new Color3(1.0f, 0.86783814f, 0.7548521f)", "Editor scene should keep the warm sun diffuse as the target linear GPU color");
        AssertContains(viewportGame, "MapSunDiffuseForStrideColorProvider = MapSunDiffuseLinear.ToSRgb()", "Editor scene should compensate Stride LightComponent gamma-space SetColor input");
        AssertContains(viewportGame, "MapSunIntensity = 20.0f", "Editor scene should use map sun intensity");
        AssertContains(viewportGame, "MapToSunDirection = new Vector3(-0.8181818f, 0.54545456f, -0.18181819f)", "Editor scene should use map-lighting sun direction");
        AssertContains(viewportGame, "MapEnvironmentIntensity = 20.0f", "Editor scene should use cubemap intensity");
        AssertContains(viewportGame, "Quaternion.BetweenDirections(LightProcessor.DefaultDirection, -MapToSunDirection)", "Editor scene should rotate the directional light so RenderLight.Direction matches map-lighting semantics");
        AssertContains(viewportGame, "light.SetColor(MapSunDiffuseForStrideColorProvider)", "Editor scene should not pass linear sun color directly to Stride SetColor");
        AssertNotContains(viewportGame, "C" + "k3", "Editor scene map-lighting symbols should use neutral names");
        AssertNotContains(viewportGame, "C" + "K3", "Editor scene map-lighting messages should use neutral wording");
    }

    private static void BottomShaderSamplesTextureAssets()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "BottomDiffuseTexture", "RiverBottom should declare the bottom diffuse texture");
        AssertContains(shader, "BottomNormalTexture", "RiverBottom should declare the bottom normal texture");
        AssertContains(shader, "BottomPropertiesTexture", "RiverBottom should declare the bottom properties texture");
        AssertContains(shader, "BottomDiffuseTexture.Sample", "RiverBottom should sample bottom diffuse instead of relying on constant color");
        AssertContains(shader, "BottomNormalTexture.Sample", "RiverBottom should sample bottom normal for riverbed detail");
        AssertContains(shader, "BottomPropertiesTexture.Sample", "RiverBottom should sample bottom properties for riverbed material response");
    }

    private static void CommonShaderUsesCosineRiverDepthProfile()
    {
        string common = ReadRepositoryText("Terrain.Editor/Effects/RiverCommon.sdsl");

        AssertContains(common, "cos(crossSection * 2.0f * 3.14159265f) * 0.5f + 0.5f", "River depth should use the cosine-shaped cross-section profile");
        AssertNotContains(common, "abs(crossSection * 2.0f - 1.0f)", "River depth should not regress to the older parabolic ribbon profile");
    }

    private static void BottomShaderUsesTargetAdvancedUvAndParallaxSemantics()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "return _Depth * (1.0f - pow(cos(tangentUv.y * 2.0f * 3.14159265f) * 0.5f + 0.5f, 2.0f));", "RiverBottom should use the target capture's geometric cosine depth path");
        AssertContains(shader, "int maxIterations = max(_ParallaxIterations, 2);", "RiverBottom parallax should use the configured iteration count");
        AssertContains(shader, "for (int i = 0; i < maxIterations; ++i)", "RiverBottom parallax should step with the configured iteration count");
        AssertContains(shader, "float weight = nextDepth / (nextDepth - prevDepth);", "RiverBottom steep parallax should use the target interpolation formula without clamping");
        AssertContains(shader, "textureWorldUv = mapUnitPosition + mapUnitOffset;", "RiverBottom should sample textures from the target capture's world-UV path");
        AssertContains(shader, "bottomWorldPositionXZ = positionWS.xz + mapUnitOffset / max(_WorldToMapUnitScale, 0.0001f);", "RiverBottom should keep refraction depth compression in Stride world coordinates");
        AssertContains(shader, "float2 scaledRiverUv = float2(riverUv.x * _TextureUvScale, riverUv.y);", "RiverBottom advanced path should scale ribbon U before parallax and texture sampling");
        AssertContains(shader, "CalcParallaxedBottomUvs(scaledRiverUv, streams.PositionWS.xyz, tangent, bitangent, surfaceNormal, worldWidth, viewDir, textureWorldUv, bottomWorldPositionXZ, tangentUv);", "RiverBottom advanced path should feed scaled ribbon UVs into steep parallax");
        AssertContains(shader, "tangentUv = riverUv + tangentSpaceOffset;", "RiverBottom parallax should keep the tangent-space ribbon UV output");
        AssertContains(shader, "BottomDiffuseTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom advanced path should sample bottom diffuse from parallaxed tangent UVs");
        AssertContains(shader, "BottomNormalTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom advanced path should sample bottom normal from parallaxed tangent UVs");
        AssertContains(shader, "BottomPropertiesTexture.Sample(BottomTextureSampler, tangentUv)", "RiverBottom advanced path should sample bottom properties from parallaxed tangent UVs");
        AssertNotContains(shader, "safeDenom", "RiverBottom should not clamp steep parallax interpolation");
        AssertNotContains(shader, "saturate(nextDepth /", "RiverBottom should not clamp steep parallax interpolation");
        AssertNotContains(shader, "BottomDiffuseTexture.Sample(BottomTextureSampler, textureWorldUv)", "RiverBottom should no longer sample bottom diffuse from world UVs in the advanced path");
        AssertNotContains(shader, "BottomNormalTexture.Sample(BottomTextureSampler, textureWorldUv)", "RiverBottom should no longer sample bottom normal from world UVs in the advanced path");
        AssertNotContains(shader, "BottomPropertiesTexture.Sample(BottomTextureSampler, textureWorldUv)", "RiverBottom should no longer sample bottom properties from world UVs in the advanced path");
    }

    private static void BottomShaderUsesTargetAdvancedAlphaSemantics()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "stage float _WaterHeight = 3.0f;", "RiverBottom should expose water height for ocean fade");
        AssertContains(shader, "float underOceanFade = 1.0f - saturate((_WaterHeight - streams.PositionWS.y) * _OceanFadeRate);", "RiverBottom should compute underwater fade from water height and river surface height");
        AssertContains(shader, "float fadeOut = min(underOceanFade, saturate(streams.RiverTransparency));", "RiverBottom should use a dedicated fadeOut term for bottom blending");
        AssertContains(shader, "float edgeFade1 = smoothstep(0.0f, max(_BankFade, 0.0001f), riverUv.y);", "RiverBottom advanced alpha should use the bank-fade edge term on one side");
        AssertContains(shader, "float edgeFade2 = smoothstep(0.0f, max(_BankFade, 0.0001f), 1.0f - riverUv.y);", "RiverBottom advanced alpha should use the bank-fade edge term on the other side");
        AssertContains(shader, "float alpha = bottomDiffuse.a * fadeOut * connectionFade * edgeFade1 * edgeFade2;", "RiverBottom advanced alpha should follow the CK3 diffuse-alpha bank fade path");
        AssertNotContains(shader, "float edgeFade = saturate(depth * 13.0f);", "RiverBottom should no longer use the old depth-only edge fade");
        AssertNotContains(shader, "float alpha = fadeOut * connectionFade * edgeFade;", "RiverBottom should no longer use the old bottom alpha path");
    }

    private static void BottomShaderUsesTargetBottomShadowPath()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(shader, "float CalcRandom(float2 seed)", "RiverBottom should use the target random-disc shadow rotation helper");
        AssertContains(shader, "float2 RotateShadowDisc(float2 disc, float2 rotate)", "RiverBottom should rotate the CK3 disc kernel per pixel");
        AssertContains(shader, "float sunRaySurfaceIntersection = waterDepth / toSunDir.y;", "RiverBottom should intersect the sun ray with the water surface using the target depth formula");
        AssertContains(shader, "float shadowCompareDepth = min(shadowProj.z, waterSurfaceProj.z) - _SceneShadowDepthBias;", "RiverBottom should exclude the water surface from shadow casting while respecting the active scene shadow producer bias");
        AssertContains(shader, "float4 samples = GetSceneShadowDiscSample(i) * _SceneShadowKernelScale;", "RiverBottom should use the target disc sample kernel");
        AssertContains(shader, "shadowTerm = shadowTerm / 8.0f;", "RiverBottom should average the two-samples-per-entry CK3 shadow kernel");
        AssertContains(shader, "float shadow = EvaluateSceneShadow(positionWS, waterDepth, lightDir);", "RiverBottom direct lighting should use the target bottom shadow evaluation");
        AssertContains(shader, "float3 fadeFactor = saturate(float3(1.0f - abs(0.5f - shadowProj.xy) * 2.0f, 1.0f - shadowProj.z) * fadeStrength);", "RiverBottom should fade shadowing near cascade edges like the target path");
        AssertNotContains(shader, "Get5x5SceneShadowFilterKernel", "RiverBottom should no longer use the old 5x5 shadow kernel helper");
        AssertNotContains(shader, "FilterSceneShadow5x5", "RiverBottom should no longer use the old fixed 5x5 shadow filter");
        AssertNotContains(shader, "GetSceneShadowPositionOffset", "RiverBottom should not use a normal-offset shadow substitute for the bottom water-surface exclusion");
    }

    private static void BottomShaderUsesSceneMaterialLighting()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");

        AssertContains(shader, "TextureCube<float4> EnvironmentMapTexture", "RiverBottom should bind a cube environment map for bottom lighting");
        AssertContains(shader, "EnvironmentMapTexture.SampleLevel", "RiverBottom should sample environment IBL instead of baking brightness into diffuse");
        AssertContains(shader, "CalculateRiverBottomLighting", "RiverBottom should route bottom albedo through a material lighting function");
        AssertContains(shader, "float specularColor = 0.25f * saturate(properties.g);", "RiverBottom should remap bottom specular to material specular color");
        AssertContains(shader, "RiverD_GGX", "RiverBottom direct specular should use a GGX distribution");
        AssertContains(shader, "RiverV_Optimized", "RiverBottom direct specular should use the optimized visibility term");
        AssertContains(shader, "RiverGetSpecularDominantDir", "RiverBottom specular IBL should use dominant reflection direction");
        AssertContains(shader, "RiverBurleyToMipSimple", "RiverBottom specular IBL should map perceptual roughness to cubemap mip");
        AssertContains(shader, "float maxEnvironmentMip = max(_EnvironmentMipCount - 3.0f, 0.0f);", "RiverBottom IBL should use the target max mip formula including the mip-zero offset");
        AssertContains(shader, "_SceneSunDirection", "RiverBottom should bind the visible scene directional-light direction");
        AssertContains(shader, "_SceneSunColor", "RiverBottom should bind the visible scene directional-light color");
        AssertContains(shader, "_SceneWorldToShadowCascadeUV", "RiverBottom should bind the real scene world-to-shadow matrices");
        AssertContains(shader, "float shadow = EvaluateSceneShadow(positionWS, waterDepth, lightDir);", "RiverBottom direct light should route through the target bottom shadow projection");
        AssertContains(shader, "RotateEnvironmentSampleDirection", "RiverBottom should centralize scene skybox rotation for IBL sampling");
        AssertContains(shader, "return mul((float3x3)_EnvironmentSkyMatrix, sampleDirection);", "RiverBottom IBL rotation should match the target cubemap rotation without flipping Z");
        AssertContains(shader, "RiverUnpackRRxGNormal", "RiverBottom should use packed bottom normals");
        AssertContains(shader, "float3 tangent = normalize(streams.RiverTangent);", "RiverBottom should use the input tangent orientation directly for bottom normal-map TBN");
        AssertContains(shader, "parallaxNormal.x * tangent + parallaxNormal.y * bitangent + parallaxNormal.z * surfaceNormal", "RiverBottom should transform the sampled bottom normal through the river tangent basis");
        AssertNotContains(shader, "bottomNormal = normalize(RotateNormalToTerrain(bottomNormal, surfaceNormal));", "RiverBottom should not apply the advanced terrain-normal rotation in the target capture's old bottom path");
        AssertContains(shader, "CalculateRiverBottomLighting(bottomLightingPosition, worldDepth, bottomDiffuse.rgb, bottomNormal, bottomViewDir, bottomProperties);", "RiverBottom should light the submerged bottom world position before fake-depth compression");
        AssertContains(feature, "RiverBottomKeys.EnvironmentMapTexture", "RiverRenderFeature should bind the environment cubemap to the bottom pass");
        AssertNotContains(shader, "_BottomSunDirection", "RiverBottom should not keep a river-local sun direction once bottom lighting is scene-driven");
        AssertNotContains(shader, "_ShadowTermFallback", "RiverBottom should not keep the old neutral-lighting shadow fallback once bottom lighting is scene-driven");
        AssertNotContains(shader, "bottomDiffuse.rgb * depthTint * 2.0f", "RiverBottom should not darken albedo by depth tint as a substitute for material lighting");
    }

    private static void BottomShaderDoesNotApplyGlobalLightingEnergyBoost()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");

        AssertContains(
            shader,
            "float3 color = CalculateRiverBottomLighting(bottomLightingPosition, worldDepth, bottomDiffuse.rgb, bottomNormal, bottomViewDir, bottomProperties);",
            "RiverBottom should write the lit bottom color without a global energy multiplier");
        AssertNotContains(
            shader,
            "CalculateRiverBottomLighting(bottomLightingPosition, worldDepth, bottomDiffuse.rgb, bottomNormal, bottomViewDir, bottomProperties) * 3.0f",
            "RiverBottom should not reintroduce the old global 3x lighting gain");
    }

    private static void RenderObjectCarriesRiverSettingsToShaderBinding()
    {
        string renderObject = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderObject.cs");
        string settings = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderSettings.cs");
        string processor = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverProcessor.cs");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");

        AssertContains(settings, "public float FlattenMultiplier { get; set; } = 1.0f;", "RiverRenderSettings should expose water normal flattening");
        AssertContains(settings, "public float BottomNormalStrength { get; set; } = 1.0f;", "RiverRenderSettings should expose bottom normal strength for bottom lighting");
        AssertContains(settings, "public float BottomEnvironmentIntensity { get; set; }", "RiverRenderSettings should expose bottom environment intensity");
        AssertContains(renderObject, "public float FlattenMultiplier { get; private set; } = 1.0f;", "RiverRenderObject should cache water normal flattening");
        AssertContains(renderObject, "FlattenMultiplier = settings.FlattenMultiplier;", "RiverRenderObject should copy water normal flattening from RiverRenderSettings");
        AssertContains(renderObject, "public Vector2 MapWorldSize { get; private set; } = new(4096.0f, 4096.0f);", "RiverRenderObject should cache per-axis map world size for rectangular map UV normalization");
        AssertContains(renderObject, "public int ParallaxIterations { get; private set; } = 10;", "RiverRenderObject should cache parallax iteration count for advanced paths");
        AssertContains(renderObject, "MapWorldSize = mesh.MapWorldSize;", "RiverRenderObject should keep the per-axis map world size from the generated river mesh");
        AssertContains(processor, "renderObject.ApplySettings(component.Settings);", "RiverProcessor should push component settings into each render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._WaterHeight, 3.0f);", "RiverRenderFeature should bind water height for bottom ocean fade");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._TextureUvScale, riverObject.TextureUvScale);", "RiverRenderFeature should continue binding texture UV scale for available shader variants");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._OceanFadeRate, riverObject.OceanFadeRate);", "RiverRenderFeature should bind bottom ocean fade rate from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._WorldToMapUnitScale, 0.5f);", "RiverRenderFeature should bind the local world-to-map-unit conversion for world-UV bottom sampling");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._BottomNormalStrength, riverObject.BottomNormalStrength);", "RiverRenderFeature should bind bottom normal strength from the render object");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._ViewMatrix, viewMatrix);", "RiverRenderFeature should bind the view matrix required by view-space refraction offset");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._GlobalTime, globalTime);", "RiverRenderFeature should bind animated water time");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._FlattenMult, riverObject.FlattenMultiplier);", "RiverRenderFeature should bind water normal flattening");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._MapWorldSize, riverObject.MapWorldSize);", "RiverRenderFeature should bind per-axis map world size into the surface shader");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionScale, riverObject.WaterRefractionScale);", "RiverRenderFeature should bind refraction scale from the render object");
        AssertNotContains(feature, "_BottomSpecularIntensity", "RiverRenderFeature should not bind river-local bottom specular intensity");
    }

    private static void BottomLightingInputsComeFromSceneBindings()
    {
        string loader = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverResourceLoader.cs");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");
        string viewportGame = ReadRepositoryText("Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs");

        AssertContains(feature, "bottomShadowMapRenderer = forwardLightingFeature?.ShadowMapRenderer;", "RiverRenderFeature should grab Stride's shadow-map renderer from forward lighting");
        AssertContains(feature, "var lightingView = renderView.LightingView ?? renderView;", "RiverRenderFeature should resolve bottom lighting from the current lighting view");
        AssertContains(feature, "SelectBottomDirectionalLight(lights, renderViewLightData, lightingView)", "RiverRenderFeature should choose the bottom directional light through a dedicated selection helper");
        AssertContains(feature, "LightDirectionalShadowMapRenderer.ShaderData", "RiverRenderFeature should read Stride's directional shadow shader data for the bottom pass");
        AssertContains(feature, "RiverBottomKeys._SceneSunDirection", "RiverRenderFeature should bind the scene directional-light direction for the bottom pass");
        AssertContains(feature, "RiverBottomKeys._SceneSunColor", "RiverRenderFeature should bind the scene directional-light color for the bottom pass");
        AssertContains(feature, "sceneShadowDepthBias = shaderData.DepthBias;", "RiverRenderFeature should carry Stride's active shadow depth bias into the bottom compare path");
        AssertContains(feature, "RiverBottomKeys._SceneShadowDepthBias", "RiverRenderFeature should bind the active scene shadow depth bias for the bottom pass");
        AssertContains(feature, "RiverBottomKeys._SceneWorldToShadowCascadeUV", "RiverRenderFeature should bind the real scene world-to-shadow matrices");
        AssertContains(feature, "RiverBottomKeys.SceneShadowMapTexture", "RiverRenderFeature should bind the real scene shadow atlas texture");
        AssertContains(feature, "SkyboxKeys.CubeMap", "RiverRenderFeature should read the real scene skybox specular cubemap");
        AssertContains(feature, "River bottom requires a real scene skybox cubemap.", "RiverRenderFeature should reject non-CK3 bottom environment fallbacks");
        AssertContains(feature, "RiverBottomKeys._EnvironmentSkyMatrix", "RiverRenderFeature should bind scene skybox rotation for bottom IBL");
        AssertContains(feature, "RiverBottomKeys._EnvironmentIntensity", "RiverRenderFeature should bind scene skybox intensity for bottom IBL");
        AssertContains(feature, "surfaceEffect.Parameters.Set(RiverSurfaceKeys.WaterColorSampler, graphicsDevice.SamplerStates.LinearWrap);", "RiverRenderFeature should bind a dedicated water-color sampler");
        AssertContains(feature, "SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.ReflectionSpecularTexture, riverResources.ReflectionSpecular);", "RiverRenderFeature should keep the river reflection/specular asset on the surface pass");
        AssertContains(viewportGame, "_riverComponent.Settings.BottomEnvironmentIntensity = 1.0f;", "EmbeddedStrideViewportGame should keep only bottom environment tuning as an explicit river-side multiplier");
        AssertNotContains(feature, "RiverBottomEffectKeys.BottomLightGroup", "RiverRenderFeature should not depend on a brittle light-group permutation");
        AssertNotContains(feature, "RiverBottomKeys._ShadowTermFallback", "RiverRenderFeature should not bind the old river-local shadow fallback into the bottom pass");
        AssertNotContains(loader, "BottomEnvironmentUrl", "RiverResourceLoader should not keep a bottom environment fallback URL once bottom IBL is scene-only");
        AssertNotContains(loader, "EnsureBottomEnvironment", "RiverResourceLoader should not keep a bottom environment fallback loader once bottom IBL is scene-only");
        AssertNotContains(feature, "riverResources.EnsureBottomEnvironment", "RiverRenderFeature should not fall back to an optional skybox texture for bottom IBL");
        AssertNotContains(feature, "bottomEnvironment ??= riverResources.ReflectionSpecular", "RiverRenderFeature should not use the surface reflection fallback cubemap for bottom IBL");
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
        AssertContains(shader, "TextureCube<float4> ReflectionSpecularTexture", "RiverSurface should treat reflection-specular as a cubemap");
        AssertContains(shader, "WaterColorSampler", "RiverSurface should declare a dedicated sampler for map-space water-color lookups");
        AssertContains(shader, "WaterColorTexture", "RiverSurface should declare water color/spec texture");
        AssertContains(shader, "FlowNormalTexture.Sample", "RiverSurface should use texture-driven flow normals");
        AssertContains(shader, "SampleNormalMapTexture(AmbientNormalTexture", "RiverSurface should use ambient normal ripples");
        AssertContains(shader, "SampleWaterColorTexture", "RiverSurface should use the dedicated water color/spec sampling path");
        AssertContains(shader, "colorAndSpec.rgb = DecodeWaterColorSrgb(colorAndSpec.rgb);", "RiverSurface should manually decode water-color RGB after importing the asset as UNORM");
        AssertContains(shader, "FoamNoiseTexture.Sample", "RiverSurface should use foam noise");
        AssertContains(shader, "FoamMapTexture.Sample", "RiverSurface should use foam map shaping");
        AssertContains(shader, "FoamTexture.Sample", "RiverSurface should use foam detail texture");
        AssertContains(shader, "float foamRampU = clamp(foamFactor * FlowFoamMask, 0.5f / 256.0f, 1.0f - 0.5f / 256.0f);", "RiverSurface should avoid wrap bleed at the black edge of the foam ramp");
        AssertContains(shader, "FoamRampTexture.SampleLevel(WaterTextureSampler, float2(foamRampU, 0.5f), 0.0f)", "RiverSurface should sample the foam ramp at lod0 like the target path");
        AssertContains(shader, "CalcFoamFactor(InputWorldUV, InputWorldSpacePos.xz, InputDepth, InputFlowFoamMask, InputFlowNormal)", "RiverSurface foam detail should use world-space XZ like CK3, not map-unit XZ");
        AssertContains(shader, "ReflectionSpecularTexture.Sample", "RiverSurface should add asset-driven reflection variation");
    }

    private static void SurfaceShaderUsesTargetWaterNormalsAndFlow()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");

        AssertContains(shader, "stage float _FlattenMult = 1.0f;", "RiverSurface should expose normal flattening");
        AssertContains(shader, "stage float2 _WaterWave1Scale = float2(10.0f, 10.0f);", "RiverSurface should expose the first ambient wave scale");
        AssertContains(shader, "stage float2 _WaterWave2Scale = float2(2.0f, 1.0f);", "RiverSurface should expose the second ambient wave scale");
        AssertContains(shader, "stage float2 _WaterWave3Scale = float2(0.2f, 0.1f);", "RiverSurface should expose the third ambient wave scale");
        AssertContains(shader, "stage float _WaterFlowNormalFlatten = 1.5f;", "RiverSurface should expose flow normal flattening");
        AssertContains(shader, "float3 SampleNormalMapTexture(", "RiverSurface should sample ambient normal layers through the target normal-map helper");
        AssertContains(shader, "return packedNormal.xyz - 0.5f;", "RiverSurface should decode target water normals as centered texture values");
        AssertContains(shader, "float3 normal = DecodeWaterNormal(textureSource.Sample(WaterTextureSampler, uvCoord)).xzy;", "RiverSurface ambient waves should use target centered normal decoding");
        AssertContains(shader, "float3 normalMap1 = SampleNormalMapTexture(AmbientNormalTexture, uvCoord, _WaterWave1Scale", "RiverSurface should include ambient normal layer 1");
        AssertContains(shader, "float3 normalMap2 = SampleNormalMapTexture(AmbientNormalTexture, uvCoord, _WaterWave2Scale", "RiverSurface should include ambient normal layer 2");
        AssertContains(shader, "float3 normalMap3 = SampleNormalMapTexture(AmbientNormalTexture, uvCoord, _WaterWave3Scale", "RiverSurface should include ambient normal layer 3");
        AssertContains(shader, "flowUv *= float2(worldWidth, 1.0f) * _FlowNormalUvScale;", "RiverSurface advanced flow UV should scale by river width");
        AssertContains(shader, "return FlowNormalTexture.Sample(WaterTextureSampler, flowUv);", "RiverSurface advanced flow should use a single flow-normal sample");
        AssertContains(shader, "float3 flowNormal = DecodeWaterNormal(flowNormalSample).xzy;", "RiverSurface flow normal should use target centered normal decoding");
        AssertContains(shader, "flowNormal.y *= _WaterFlowNormalFlatten * _FlattenMult * saturate(dot(streams.RiverNormal, float3(0.0f, 1.0f, 0.0f)));", "RiverSurface should flatten the flow normal like the target advanced path");
        AssertNotContains(shader, "flowUv0.x -=", "RiverSurface advanced path should not use the old sine offset");
        AssertNotContains(shader, "flowUv1", "RiverSurface advanced path should not blend a second flow-normal sample");
        AssertNotContains(shader, "worldUv * 24.0f", "RiverSurface should not use the old two-layer ambient normal approximation");
    }

    private static void SurfaceShaderFollowsTargetRefractionAndFadeSemantics()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");

        AssertContains(shader, "float2 ComputeMapWorldUv(float3 worldPosition)", "RiverSurface should centralize map-space UV conversion");
        AssertContains(shader, "GetMapUnitXZ(worldPosition) / max(_MapWorldSize, float2(1.0f, 1.0f))", "RiverSurface should normalize map-unit UVs per axis");
        AssertContains(shader, "uv.y = 1.0f - uv.y;", "RiverSurface should flip map-space Y before sampling water-color maps");
        AssertContains(shader, "float CalcRiverProfileDepth(float2 riverUv)", "RiverSurface should use a local non-advanced river profile depth helper");
        AssertContains(shader, "return _Depth * (1.0f - pow(cos(riverUv.y * 2.0f * 3.14159265f) * 0.5f + 0.5f, 2.0f));", "RiverSurface should use the target cosine depth profile");
        AssertContains(shader, "float3 CalcRefraction(float3 WorldSpacePos, float3 Normal, float2 ScreenPos, float3 WaterColor, float Depth)", "RiverSurface should keep refraction as a dedicated water function");
        AssertContains(shader, "float4 refractionSample = RefractionTexture.Sample(RefractionSampler, screenUv);", "RiverSurface should first sample the undistorted refraction buffer");
        AssertContains(shader, "Depth = min(Depth, refractionDepth);", "RiverSurface should use the shallower depth for the refraction shore mask");
        AssertContains(shader, "float4 offsetRefractionSample = RefractionTexture.Sample(RefractionSampler, screenUv + refractionOffset);", "RiverSurface should sample offset refraction separately");
        AssertContains(shader, "float offsetStep = step(WorldSpacePos.y, offsetRefractionWorldPosition.y);", "RiverSurface should reject offset samples above the water surface");
        AssertContains(shader, "return CalcTerrainUnderwaterSeeThrough(refractionDepth, refractionWorldPosition, refractionWaterColorMap, refractionSample.rgb);", "RiverSurface see-through should use final refraction depth, not surface depth");
        AssertContains(shader, "float waterFade = ComputeWaterFade(depth);", "RiverSurface WaterFade should use the separate base-refraction depth path");
        AssertContains(shader, "float3 refractionColor = CalcRefraction(InputWorldSpacePos, waterNormal, InputScreenSpacePos.xy, waterColorAndSpec.rgb, InputDepth);", "RiverSurface should route refraction through CalcRefraction");
        AssertContains(shader, "waterColor.a = saturate(depth * 2.0f / max(_Depth, 0.0001f)) * transparency * connectionFade;", "RiverSurface final alpha should follow the target depth-based river surface fade");
        AssertNotContains(shader, "waterColor.a = edgeFade1 * edgeFade2 * connectionFade * transparency;", "RiverSurface should not use bottom-style bank-fade alpha for the surface pass");
        AssertNotContains(shader, "SampleRefractionSeeThrough", "RiverSurface should not keep the old combined refraction helper");
        AssertNotContains(shader, "ComputeRiverWaterFade", "RiverSurface should not keep the old water-fade adapter");
        AssertNotContains(shader, "_DepthFactor", "RiverSurface should not pass a cross-section depth factor into water shading");
        AssertNotContains(shader, "effectiveDepth", "RiverSurface see-through should not cap refraction depth by surface depth");
        AssertNotContains(shader, "RiverDepthFromCrossSection(riverUv.y", "RiverSurface should not use the bank-width power helper for this path");
    }

    private static void SurfaceShaderUsesTargetWaterLightingStructure()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");

        AssertContains(shader, "stage float3 _DefaultEnvironmentSunDiffuse = float3(1.0f, 0.86783814f, 0.7548521f);", "RiverSurface should expose map sun diffuse color");
        AssertContains(shader, "stage float _DefaultEnvironmentSunIntensity = 20.0f;", "RiverSurface should expose map sun intensity");
        AssertContains(shader, "stage float _WaterZoomedInZoomedOutFactor = 0.0f;", "RiverSurface should expose the target water zoom factor used by CalcWater");
        AssertContains(shader, "stage float3 _WaterToSunDir = float3(-0.543915f, 0.5933618f, 0.5933618f);", "RiverSurface should expose the target water sun direction used by CalcWater");
        AssertContains(shader, "void ImprovedBlinnPhong(", "RiverSurface should use the target water direct-light model");
        AssertContains(shader, "float fowGlossiness = lerp(_WaterGlossBase, glossMap, saturate(_WaterZoomedInZoomedOutFactor));", "RiverSurface should match target gloss interpolation");
        AssertContains(shader, "float nonLinearGlossiness = GetNonLinearGlossiness(fowGlossiness) * _WaterGlossScale;", "RiverSurface should use the target water gloss scale parameter");
        AssertContains(shader, "_DefaultEnvironmentSunDiffuse * _DefaultEnvironmentSunIntensity", "RiverSurface should light water from the map sun inputs without gloss-map gating");
        AssertContains(shader, "float3 waterColor = ComposeLight(waterDiffuse + foam, float3(0.0f, 0.0f, 0.0f), diffuseLight, specularLight * _WaterSpecularFactor);", "RiverSurface should compose diffuse and specular before refraction/reflection");
        AssertContains(shader, "float3 reflectionColor = CalcReflection(waterNormal, toCameraDir);", "RiverSurface should use target cubemap intensity directly");
        AssertNotContains(shader, "sunIntensityMask", "RiverSurface should not gate target sun lighting by the water color map gloss channel");
        AssertNotContains(shader, "GetWaterGlossScale", "RiverSurface should not keep the old shadow-mask gloss helper");
        AssertNotContains(shader, "GetWaterCubemapIntensity", "RiverSurface should not keep the old shadow-mask cubemap helper");
        AssertNotContains(shader, "lerp(0.35f, 1.0f, ndotl)", "RiverSurface should not keep the old neutral diffuse approximation");
        AssertNotContains(shader, "normalEnergy", "RiverSurface should not keep the old global normal-energy color gain");
    }

    private static void SurfaceShaderPostStepAppliesTargetShadowAndFogWrapper()
    {
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");
        string loader = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverResourceLoader.cs");

        AssertContains(shader, "stage Texture2D<float> HeightmapSlice0;", "RiverSurface should declare editor terrain height slice inputs without inheriting terrain vertex streams");
        AssertContains(shader, "stage Texture2D<float4> ShadowNoiseTexture;", "RiverSurface should declare the target shadow tint noise texture");
        AssertContains(shader, "float3 CalculateRiverTerrainNormal(float2 worldPositionXZ)", "RiverSurface should calculate terrain normals from project terrain height data");
        AssertContains(shader, "SampleEditorTerrainHeightLinear(sampleCoord + float2(-stepSize.x, 0.0f))", "RiverSurface terrain normal should sample editor height slices");
        AssertContains(shader, "float3 ApplyTerrainShadowTintWithClouds", "RiverSurface should include the target terrain shadow tint post step");
        AssertContains(shader, "float GetCloudShadowMask", "RiverSurface should include the target cloud shadow mask step");
        AssertContains(shader, "float3 ApplyMapDistanceFogWithoutFoW", "RiverSurface should include the target map distance fog post step");
        AssertContains(shader, "waterColor = ApplySurfacePostProcessing(waterColor, streams.PositionWS.xyz);", "RiverSurface PS should run the post chain after CalcRiverAdvanced");
        AssertContains(shader, "float cloudMask = GetCloudShadowMask(worldPosition.xz);", "RiverSurface post step should feed cloud shadow into final water color");
        AssertContains(shader, "color.rgb = ApplyTerrainShadowTintWithClouds(color.rgb, worldPosition.xz, cloudMask, 1.0f);", "RiverSurface post step should tint final water color from editor terrain normals");
        AssertContains(shader, "color.rgb = lerp(color.rgb, float3(0.0f, 0.01f, 0.02f), cloudMask * 0.8f);", "RiverSurface post step should blend the final water color toward the target cloudy tint");
        AssertContains(shader, "color.rgb = ApplyMapDistanceFogWithoutFoW(color.rgb, worldPosition);", "RiverSurface post step should run map distance fog without fog-of-war");
        AssertContains(shader, "color.a *= 1.0f - _FlatMapLerp;", "RiverSurface post step should keep flat-map alpha fade");
        AssertContains(shader, "color.a *= zoomBlendOut;", "RiverSurface post step should keep zoom alpha fade");
        AssertNotContains(shader, "_ShadowTermFallback", "RiverSurface should not keep the old shadow fallback parameter");
        AssertNotContains(shader, "_CloudMaskFallback", "RiverSurface should not keep the old cloud fallback parameter");
        AssertNotContains(shader, "_ZoomBlendOut", "RiverSurface should derive zoom fade from the target water zoom factor");
        AssertNotContains(shader, "FogOfWarAlphaTexture", "RiverSurface should not depend on a strategy-layer fog-of-war texture for river shading");
        AssertNotContains(shader, "FogOfWarAlphaSampler", "RiverSurface should not bind a strategy-layer fog-of-war sampler");
        AssertNotContains(shader, "_HasFogOfWarAlphaTexture", "RiverSurface should not keep a fog-of-war capability switch");
        AssertNotContains(shader, "ApplyFogOfWar", "RiverSurface should not run strategy-layer fog-of-war color adjustment");
        AssertNotContains(shader, "SampleFogOfWarAlphaRaw", "RiverSurface should not sample strategy-layer fog-of-war alpha");

        AssertContains(loader, "ShadowColorUrl = \"River/Water/shadow-color\"", "RiverResourceLoader should load the shadow tint texture through a neutral asset URL");
        AssertContains(feature, "TryBindEditorTerrainInputs()", "RiverRenderFeature should bind editor terrain height slices into the surface pass");
        AssertContains(feature, "RiverSurfaceKeys.ShadowNoiseTexture", "RiverRenderFeature should bind the shadow tint texture into the surface pass");
        AssertContains(feature, "RiverSurfaceKeys.HeightmapSlice0", "RiverRenderFeature should bind editor terrain height slices into the river surface key set");
        AssertNotContains(feature, "SurfaceFogOfWarAlphaTexture", "RiverRenderFeature should not expose fog-of-war as a river surface input");
        AssertNotContains(feature, "FogOfWarAlphaTexture", "RiverRenderFeature should not bind fog-of-war textures into the river surface pass");
        AssertNotContains(feature, "FogOfWarAlphaSampler", "RiverRenderFeature should not bind a fog-of-war sampler into the river surface pass");
        AssertNotContains(feature, "_HasFogOfWarAlphaTexture", "RiverRenderFeature should not keep a fog-of-war capability switch");
        AssertNotContains(feature, "VisibleFogAlpha", "RiverRenderFeature should not create a white fog-of-war substitute");
        AssertNotContains(feature, "FlatHeightmap", "RiverRenderFeature should not create a flat terrain-normal substitute");
    }

    private static void RenderFeatureSeparatesSceneSeedFromWorkingBuffer()
    {
        string resources = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderResources.cs");
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");

        AssertContains(resources, "public Texture? SceneSeedColor { get; private set; }", "RiverRenderResources should allocate a dedicated scene seed buffer");
        AssertContains(feature, "renderResources.SceneSeedColor", "RiverRenderFeature should use the dedicated scene seed texture");
        AssertContains(feature, "seedEffect.SetOutput(seedTarget);", "RiverRenderFeature should seed scene color into SceneSeedColor first");
        AssertContains(feature, "commandList.CopyRegion(renderResources.SceneSeedColor, 0, null, renderResources.BottomColor, 0);", "RiverRenderFeature should copy the scene seed into the working bottom/refraction buffer before the bottom pass");
        AssertContains(feature, "surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);", "RiverRenderFeature should sample refraction with the target linear clamp sampler");
        AssertContains(feature, "blendState.RenderTargets[0].AlphaSourceBlend = Blend.One;", "RiverRenderFeature should write refraction payload alpha directly instead of blending it by bottom coverage");
        AssertContains(feature, "blendState.RenderTargets[0].AlphaDestinationBlend = Blend.Zero;", "RiverRenderFeature should not preserve the scene-seed alpha under partially covered bottom pixels");
        AssertNotContains(feature, "RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.PointClamp", "RiverRenderFeature should not keep the previous point-clamp refraction sampler binding");
    }

    private static void RenderFeatureSceneSeedWritesDepthPayload()
    {
        string feature = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverRenderFeature.cs");
        string shader = ReadRepositoryText("Terrain.Editor/Effects/RiverSceneSeed.sdsl");
        string project = ReadRepositoryText("Terrain.Editor/Terrain.Editor.csproj");

        AssertContains(feature, "new ImageEffectShader(\"RiverSceneSeed\", delaySetRenderTargets: true)", "RiverRenderFeature should use the dedicated river scene-seed shader");
        AssertContains(feature, "GetPresenterSceneDepthSource(context.GraphicsDevice, seedSource)", "RiverRenderFeature should use the presenter scene depth explicitly for the current windowed editor/runtime path");
        AssertContains(feature, "Debug.Assert(presenter != null", "RiverRenderFeature should assert when presenter depth is unavailable");
        AssertContains(feature, "Debug.Assert(sceneDepth != null", "RiverRenderFeature should assert when presenter depth is unavailable");
        AssertContains(feature, "must match scene color size", "RiverRenderFeature should reject depth buffers that do not match scene color size before resolving them");
        AssertContains(feature, "context.Resolver.ResolveDepthStencil(sceneDepthSource)", "RiverRenderFeature should resolve the selected scene depth for the seed shader");
        AssertContains(feature, "seedEffect.Parameters.Set(DepthBaseKeys.DepthStencil, sceneDepth)", "RiverRenderFeature should bind scene depth into RiverSceneSeed");
        AssertContains(feature, "seedEffect.Parameters.Set(CameraKeys.ZProjection", "RiverRenderFeature should bind depth reconstruction parameters into RiverSceneSeed");
        AssertContains(feature, "seedEffect.Parameters.Set(TransformationKeys.ViewInverse", "RiverRenderFeature should bind view inverse so RiverSceneSeed can reconstruct world position");
        AssertContains(feature, "seedEffect.Parameters.Set(TransformationKeys.Eye", "RiverRenderFeature should bind camera position so RiverSceneSeed writes camera-distance alpha");
        AssertContains(shader, "float ComputeSceneDistanceFromUV(float2 uv)", "RiverSceneSeed should compute camera-distance payload rather than raw view-space depth");
        AssertContains(shader, "length(positionWS.xyz - Eye.xyz)", "RiverSceneSeed alpha should match RiverCompressWorldSpace camera-distance semantics");
        AssertContains(shader, "return float4(seedColor, sceneDistance)", "RiverSceneSeed should preserve RGB and write scene distance payload alpha");
        AssertContains(project, "<Compile Update=\"Effects\\RiverSceneSeed.sdsl.cs\">", "Terrain.Editor.csproj should compile the generated RiverSceneSeed shader key file");
        AssertNotContains(feature, "refractionSeedScaler.Color = new Color4(1.0f, 1.0f, 1.0f, 0.0f);", "River scene seed should not clear alpha to zero because the seed carries scene distance payload");
        AssertNotContains(feature, "new ImageScaler(SamplingPattern.Linear, delaySetRenderTargets: true)", "RiverRenderFeature should not use ImageScaler for river seed once depth payload is required");
    }

    private static void BottomShaderPacksRefractionDistanceForSurfaceSeeThrough()
    {
        string common = ReadRepositoryText("Terrain.Editor/Effects/RiverCommon.sdsl");
        string bottom = ReadRepositoryText("Terrain.Editor/Effects/RiverBottom.sdsl");
        string surface = ReadRepositoryText("Terrain.Editor/Effects/RiverSurface.sdsl");

        AssertContains(common, "float RiverCompressWorldSpace(float3 worldPosition, float3 cameraPosition)", "RiverCommon should pack camera-relative distance");
        AssertContains(common, "float3 RiverDecompressWorldSpace(float3 surfaceWorldPosition, float compressedDistance, float3 cameraPosition)", "RiverCommon should provide the matching surface-side decompression");
        AssertContains(common, "const float maxHeight = 50.0f;", "RiverCommon should clamp refraction distance packing to CK3's maximum camera height");
        AssertContains(common, "cameraPosition = cameraPosition - toCameraDir * (above / toCameraDir.y);", "RiverCommon should project the camera down to CK3's refraction clamp plane before packing or unpacking");
        AssertContains(bottom, "stage float3 _CameraWorldPosition = float3(0.0f, 0.0f, 0.0f);", "RiverBottom should declare an explicit camera position parameter");
        AssertContains(surface, "stage float3 _CameraWorldPosition = float3(0.0f, 0.0f, 0.0f);", "RiverSurface should declare an explicit camera position parameter");
        AssertContains(bottom, "float3 bottomWorldPosition;", "RiverBottom should build the submerged bottom world position from a dedicated variable");
        AssertContains(bottom, "bottomWorldPosition.xz = bottomWorldPositionXZ;", "RiverBottom should place the submerged bottom position at the parallaxed Stride-world position");
        AssertContains(bottom, "bottomWorldPosition.y = streams.PositionWS.y - worldDepth;", "RiverBottom should drop the submerged bottom position by the computed world depth");
        AssertContains(bottom, "float compressedWorld = RiverCompressWorldSpace(bottomWorldPosition, _CameraWorldPosition);", "RiverBottom should pack bottom position distance into refraction alpha using the render feature supplied camera position");
        AssertContains(surface, "float3 toCameraDir = normalize(_CameraWorldPosition - InputWorldSpacePos);", "RiverSurface should compute facing from the explicit camera position inside CalcWater");
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
