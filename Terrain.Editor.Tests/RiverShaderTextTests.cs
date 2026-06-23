namespace Terrain.Editor.Tests;

internal static class RiverShaderTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("river texture files exist in game map water", RiverTextureFilesExistInGameMapWater);
        TestHarness.Run("river bottom and water textures are not bundle roots", RiverBottomAndWaterTexturesAreNotBundleRoots);
        TestHarness.Run("river reflection specular asset remains a cubemap", ReflectionSpecularAssetRemainsCubemap);
        TestHarness.Run("river bottom lighting uses map scene environment inputs", BottomLightingUsesMapSceneEnvironmentInputs);
        TestHarness.Run("editor tonemap uses fixed exposure for map lighting", EditorToneMapUsesFixedExposureForMapLighting);
        TestHarness.Run("editor camera uses target near clip for river depth bias", EditorCameraUsesTargetNearClipForRiverDepthBias);
        TestHarness.Run("river bottom shader samples texture assets", BottomShaderSamplesTextureAssets);
        TestHarness.Run("river common shader uses cosine river depth profile", CommonShaderUsesCosineRiverDepthProfile);
        TestHarness.Run("river bottom shader uses target advanced uv and parallax semantics", BottomShaderUsesTargetAdvancedUvAndParallaxSemantics);
        TestHarness.Run("river bottom shader uses target advanced alpha semantics", BottomShaderUsesTargetAdvancedAlphaSemantics);
        TestHarness.Run("river bottom shader uses target bottom shadow path", BottomShaderUsesTargetBottomShadowPath);
        TestHarness.Run("river shared lighting shader follows Stride standard material equations", SharedLightingShaderFollowsStrideStandardMaterialEquations);
        TestHarness.Run("river shared lighting shader numeric helpers match Stride formulas", SharedLightingShaderNumericHelpersMatchStrideFormulas);
        TestHarness.Run("river bottom shader uses shared Stride lighting", BottomShaderUsesSharedStrideLighting);
        TestHarness.Run("river bottom shader does not apply a global lighting energy boost", BottomShaderDoesNotApplyGlobalLightingEnergyBoost);
        TestHarness.Run("river render objects carry settings into shader binding", RenderObjectCarriesRiverSettingsToShaderBinding);
        TestHarness.Run("river surface shader uses shared Stride lighting", SurfaceShaderUsesSharedStrideLighting);
        TestHarness.Run("river scene lighting inputs bind to bottom and surface", RiverSceneLightingInputsBindToBottomAndSurface);
        TestHarness.Run("river shared lighting shader is registered for Stride key generation", SharedLightingShaderIsRegisteredForStrideKeyGeneration);
        TestHarness.Run("river surface shader samples water texture assets", SurfaceShaderSamplesWaterTextureAssets);
        TestHarness.Run("river surface shader uses target water normals and flow", SurfaceShaderUsesTargetWaterNormalsAndFlow);
        TestHarness.Run("river surface shader follows target refraction and fade semantics", SurfaceShaderFollowsTargetRefractionAndFadeSemantics);
        TestHarness.Run("river surface shader uses target advanced alpha branch", SurfaceShaderUsesTargetAdvancedAlphaBranch);
        TestHarness.Run("river surface shader does not apply removed post wrapper", SurfaceShaderDoesNotApplyRemovedPostWrapper);
        TestHarness.Run("river render feature separates scene seed from working refraction buffer", RenderFeatureSeparatesSceneSeedFromWorkingBuffer);
        TestHarness.Run("river scene seed writes depth payload instead of clearing alpha", RenderFeatureSceneSeedWritesDepthPayload);
        TestHarness.Run("river bottom shader packs refraction distance for surface see through", BottomShaderPacksRefractionDistanceForSurfaceSeeThrough);
        TestHarness.Run("river resource loader does not silently ignore missing textures", ResourceLoaderDoesNotSilentlyIgnoreMissingTextures);
    }

    private static void RiverTextureFilesExistInGameMapWater()
    {
        string[] fileNames =
        [
            "bottom_diffuse.dds",
            "bottom_normal.dds",
            "bottom_properties.dds",
            "bottom_depth.dds",
            "ambient_normal.dds",
            "flow_normal.dds",
            "foam.dds",
            "foam_ramp.dds",
            "foam_map.dds",
            "foam_noise.dds",
            "water_color.dds",
        ];

        foreach (string fileName in fileNames)
        {
            string fullPath = GetRepositoryPath($"game/map/water/{fileName}");
            TestHarness.Assert(File.Exists(fullPath), $"{fileName} should exist in game/map/water for direct river texture loading");
            TestHarness.Assert(new FileInfo(fullPath).Length > 0, $"{fileName} should not be empty");
        }
    }

    private static void RiverBottomAndWaterTexturesAreNotBundleRoots()
    {
        string editorPackage = ReadRepositoryText("Terrain.Editor/Terrain.Editor.sdpkg");
        string terrainPackage = ReadRepositoryText("Terrain/Terrain.sdpkg");

        string[] removedUrls =
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
        ];

        foreach (string url in removedUrls)
        {
            AssertNotContains(editorPackage, $":{url}", $"{url} should not be a RootAsset because river Bottom/Water textures are direct files under game/map/water");
            AssertNotContains(terrainPackage, $":{url}", $"{url} should not be a RootAsset because river Bottom/Water textures are direct files under game/map/water");
        }

        AssertContains(terrainPackage, ":River/Environment/reflection-specular", "River environment reflection cubemap should remain a Stride RootAsset");
    }

    private static void ReflectionSpecularAssetRemainsCubemap()
    {
        string assetPath = GetRepositoryPath("Terrain/Assets/River/Environment/reflection-specular.dds");
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

    private static void EditorToneMapUsesFixedExposureForMapLighting()
    {
        string viewportGame = ReadRepositoryText("Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs").Replace("\r\n", "\n");

        AssertContains(viewportGame, "EditorToneMapExposureEv = -2.0f", "Editor viewport should use a stable manual exposure near the no-terrain capture baseline");
        AssertContains(viewportGame, "_graphicsCompositor = compositorAsset;\n        ConfigureEditorToneMap(_graphicsCompositor);", "Asset-loaded editor compositor should configure tonemap before rendering");
        AssertContains(viewportGame, "graphicsProfile: GraphicsDevice.Features.CurrentProfile);\n        ConfigureEditorToneMap(_graphicsCompositor);", "Programmatic editor compositor should configure tonemap before rendering");
        AssertContains(viewportGame, "FindPostProcessingEffects(compositor.Game)", "Editor tonemap should find post effects through the compositor renderer tree");
        AssertContains(viewportGame, "case ForwardRenderer { PostEffects: PostProcessingEffects postEffects }:", "Editor tonemap should read post effects from the forward renderer");
        AssertContains(viewportGame, "case PresenterViewportSceneRenderer presenterRenderer:", "Editor tonemap should tolerate the presenter wrapper around the compositor game renderer");
        AssertContains(viewportGame, "case SceneRendererCollection sceneRendererCollection:", "Editor tonemap should traverse renderer collections");
        AssertNotContains(viewportGame, "compositor.SingleView?.PostEffects", "GraphicsCompositor.SingleView is only an ISceneRenderer and cannot be used to access PostEffects directly");
        AssertContains(viewportGame, "toneMap.AutoExposure = false;", "Editor tonemap should not let bright terrain lower river exposure");
        AssertContains(viewportGame, "toneMap.AutoKeyValue = false;", "Editor tonemap should not derive key value from terrain luminance");
        AssertContains(viewportGame, "toneMap.TemporalAdaptation = false;", "Editor tonemap should not retain frame-history exposure changes");
        AssertContains(viewportGame, "toneMap.Exposure = EditorToneMapExposureEv;", "Editor tonemap should bind the fixed exposure value");
    }

    private static void EditorCameraUsesTargetNearClipForRiverDepthBias()
    {
        string viewportGame = ReadRepositoryText("Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs").Replace("\r\n", "\n");
        string renderFeature = ReadRepositoryText("Terrain/Rendering/River/RiverRenderFeature.cs");
        string mainScene = ReadRepositoryText("Terrain/Assets/MainScene.sdscene");

        AssertContains(viewportGame, "EditorCameraNearClip = 10.0f", "Editor camera near clip should match the target water depth-bias distribution");
        AssertContains(viewportGame, "EditorCameraFarClip = 100000.0f", "Editor camera far clip should keep the large terrain view range");
        AssertContains(viewportGame, "ConfigureEditorCameraClipPlanes(_camera);", "Asset-loaded editor scene should override the camera clip planes");
        AssertContains(viewportGame, "NearClipPlane = EditorCameraNearClip", "Fallback editor scene should use the shared near clip");
        AssertContains(viewportGame, "FarClipPlane = EditorCameraFarClip", "Fallback editor scene should use the shared far clip");
        AssertContains(viewportGame, "RemoveNonEditorSceneComponents(editorEntity);", "Asset-loaded editor scene should not keep runtime camera scripts that compete with the embedded editor camera controller");
        AssertContains(viewportGame, "component is not TransformComponent", "Editor scene cloning should preserve transforms");
        AssertContains(viewportGame, "component is not CameraComponent", "Editor scene cloning should preserve camera components");
        AssertContains(viewportGame, "component is not LightComponent", "Editor scene cloning should preserve lighting components");
        AssertContains(viewportGame, "component is not BackgroundComponent", "Editor scene cloning should preserve background components");
        AssertContains(mainScene, "NearClipPlane: 10.0", "MainScene camera asset should also serialize the target near clip so stale/default asset data cannot keep near at 0.1");
        AssertContains(mainScene, "FarClipPlane: 100000.0", "MainScene camera asset should preserve the large terrain far clip");
        AssertContains(renderFeature, "SurfaceDepthBias = -50000", "River surface should use the target raw depth bias once the editor near clip matches the target distribution");
    }

    private static void BottomShaderSamplesTextureAssets()
    {
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverBottom.sdsl");

        AssertContains(shader, "BottomDiffuseTexture", "RiverBottom should declare the bottom diffuse texture");
        AssertContains(shader, "BottomNormalTexture", "RiverBottom should declare the bottom normal texture");
        AssertContains(shader, "BottomPropertiesTexture", "RiverBottom should declare the bottom properties texture");
        AssertContains(shader, "BottomDiffuseTexture.Sample", "RiverBottom should sample bottom diffuse instead of relying on constant color");
        AssertContains(shader, "BottomNormalTexture.Sample", "RiverBottom should sample bottom normal for riverbed detail");
        AssertContains(shader, "BottomPropertiesTexture.Sample", "RiverBottom should sample bottom properties for riverbed material response");
    }

    private static void CommonShaderUsesCosineRiverDepthProfile()
    {
        string common = ReadRepositoryText("Terrain/Effects/River/RiverCommon.sdsl");

        AssertContains(common, "cos(crossSection * 2.0f * 3.14159265f) * 0.5f + 0.5f", "River depth should use the cosine-shaped cross-section profile");
        AssertNotContains(common, "abs(crossSection * 2.0f - 1.0f)", "River depth should not regress to the older parabolic ribbon profile");
    }

    private static void BottomShaderUsesTargetAdvancedUvAndParallaxSemantics()
    {
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverBottom.sdsl");

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
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverBottom.sdsl");

        AssertContains(shader, "stage float _WaterHeight", "RiverBottom should expose water height for ocean fade");
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
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverStrideLighting.sdsl");

        AssertContains(shader, "float RiverStrideCalcRandom(float2 seed)", "RiverBottom should use the target random-disc shadow rotation helper through shared river lighting");
        AssertContains(shader, "float2 RiverStrideRotateShadowDisc(float2 disc, float2 rotate)", "RiverBottom should rotate the CK3 disc kernel per pixel through shared river lighting");
        AssertContains(shader, "float sunRaySurfaceIntersection = waterDepth / toSunDir.y;", "RiverBottom should intersect the sun ray with the water surface using the target depth formula");
        AssertContains(shader, "float shadowCompareDepth = min(shadowProj.z, waterSurfaceProj.z) - _SceneShadowDepthBias;", "RiverBottom should exclude the water surface from shadow casting while respecting the active scene shadow producer bias");
        AssertContains(shader, "float4 samples = RiverStrideGetSceneShadowDiscSample(i) * _SceneShadowKernelScale;", "RiverBottom should use the target disc sample kernel through shared river lighting");
        AssertContains(shader, "shadowTerm = shadowTerm / 8.0f;", "RiverBottom should average the two-samples-per-entry CK3 shadow kernel");
        AssertContains(shader, "float3 fadeFactor = saturate(float3(1.0f - abs(0.5f - shadowProj.xy) * 2.0f, 1.0f - shadowProj.z) * fadeStrength);", "RiverBottom should fade shadowing near cascade edges like the target path");
        AssertNotContains(shader, "Get5x5SceneShadowFilterKernel", "RiverBottom should no longer use the old 5x5 shadow kernel helper");
        AssertNotContains(shader, "FilterSceneShadow5x5", "RiverBottom should no longer use the old fixed 5x5 shadow filter");
        AssertNotContains(shader, "GetSceneShadowPositionOffset", "RiverBottom should not use a normal-offset shadow substitute for the bottom water-surface exclusion");
    }

    private static void SharedLightingShaderFollowsStrideStandardMaterialEquations()
    {
        string shaderPath = GetRepositoryPath("Terrain/Effects/River/RiverStrideLighting.sdsl");
        TestHarness.Assert(File.Exists(shaderPath), "RiverStrideLighting.sdsl should exist before shared lighting equation checks");
        string shader = File.ReadAllText(shaderPath);

        AssertContains(shader, "shader RiverStrideLighting : RiverWaterCommon", "RiverStrideLighting should be a shared mixin available to both river passes");
        AssertContains(shader, "float3 RiverStrideComputeDirectDiffuse(float3 diffuseColor, float3 lightColorNdotL)", "RiverStrideLighting should centralize Stride Lambert direct diffuse");
        AssertContains(shader, "return diffuseColor * lightColorNdotL;", "Direct Lambert diffuse should match Stride after MaterialSurfaceLightingAndShading applies PI");
        AssertContains(shader, "float RiverStrideNormalDistributionGGX(float alphaRoughness, float nDotH)", "RiverStrideLighting should use GGX normal distribution");
        AssertContains(shader, "float RiverStrideVisibilitySmithSchlickGGX(float alphaRoughness, float nDotL, float nDotV)", "RiverStrideLighting should use Stride-style Smith-Schlick visibility");
        AssertContains(shader, "return visibilityL * visibilityV / max(nDotL * nDotV, 0.00001f);", "Smith-Schlick visibility should match Stride's visibility term, not only the geometric attenuation");
        AssertContains(shader, "float3 RiverStrideFresnelSchlick(float3 specularColor, float lDotH)", "RiverStrideLighting should use Schlick Fresnel");
        AssertContains(shader, "return 3.14159265f * specular * lightColorNdotL;", "Direct microfacet specular should include Stride's final direct-light PI multiplier");
        AssertContains(shader, "float2 RiverStrideDFGPolynomial(float3 specularColor, float alphaRoughness, float nDotV)", "RiverStrideLighting should use the Stride polynomial DFG path for environment specular");
        AssertContains(shader, "float x = 1.0f - alphaRoughness;", "Stride DFG polynomial should use one minus alpha roughness as x");
        AssertContains(shader, "float y = nDotV;", "Stride DFG polynomial should use NdotV directly as y");
        AssertContains(shader, "bias *= saturate(50.0f * specularColor.y);", "Stride DFG polynomial should dampen bias by the specular color");
        AssertContains(shader, "sqrt(alphaRoughness) * _EnvironmentMipCount", "Stride roughness cubemap sampling should use sqrt(alpha roughness) times mip count");
        AssertContains(shader, "sampleDirection = mul(sampleDirection, (float3x3)_EnvironmentSkyMatrix);", "Skybox rotation should match Stride LightSkyboxShader's vector-matrix order");
        AssertContains(shader, "float diffuseMip = max(_EnvironmentMipCount - 1.0f, 0.0f);", "River diffuse IBL should sample the skybox's lowest-frequency mip");
        AssertContains(shader, "return diffuseRadiance * diffuseColor * _EnvironmentIntensity * environmentIntensityScale;", "River diffuse IBL should keep shadowed river beds visible under scene environment light");
        AssertContains(shader, "sampleDirection = float3(sampleDirection.xy, -sampleDirection.z);", "Skybox sampling should match Stride LightSkyboxShader's cubemap Z flip");
    }

    private static void SharedLightingShaderNumericHelpersMatchStrideFormulas()
    {
        float visibility = VisibilitySmithSchlickGgx(alphaRoughness: 0.36f, nDotL: 0.42f, nDotV: 0.68f);
        AssertNearlyEqual(2.585333f, visibility, 0.00001f, "RiverStride visibility should match Stride BRDFMicrofacet.VisibilitySmithSchlickGGX");

        (float scale, float bias) = DfgPolynomial(specularY: 0.01f, alphaRoughness: 0.36f, nDotV: 0.3f);
        AssertNearlyEqual(0.846103f, scale, 0.00001f, "RiverStride DFG scale should match Stride polynomial");
        AssertNearlyEqual(0.076949f, bias, 0.00001f, "RiverStride DFG bias should include Stride's specular-color damping");

        float mip = MathF.Sqrt(0.36f) * 10.0f;
        AssertNearlyEqual(6.0f, mip, 0.00001f, "RiverStride specular IBL mip should match RoughnessCubeMapEnvironmentColor");
    }

    private static void BottomShaderUsesSharedStrideLighting()
    {
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverBottom.sdsl");

        AssertContains(shader, "shader RiverBottom : ShaderBase, TransformationWAndVP, RiverVertexStreams, RiverWaterCommon, RiverStrideLighting", "RiverBottom should mix in shared Stride lighting");
        AssertContains(shader, "float shadow = RiverStrideEvaluateSceneShadow(positionWS, waterDepth, lightDir, streams.DepthVS);", "RiverBottom should preserve water-depth-aware scene shadowing");
        AssertContains(shader, "return RiverStrideComputeLighting(diffuseColor, specularColor, glossiness, normal, viewDir, shadow, _BottomEnvironmentIntensity);", "RiverBottom should route final lighting through the shared Stride helper");
        AssertNotContains(shader, "RiverD_GGX", "RiverBottom should not keep its separate GGX implementation");
        AssertNotContains(shader, "RiverV_Optimized", "RiverBottom should not keep its separate visibility implementation");
        AssertNotContains(shader, "RiverGetSpecularDominantDir", "RiverBottom should not keep its separate dominant-direction IBL implementation");
    }

    private static void SurfaceShaderUsesSharedStrideLighting()
    {
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverSurface.sdsl");

        AssertContains(shader, "shader RiverSurface : ShaderBase, TransformationWAndVP, RiverVertexStreams, RiverWaterCommon, RiverStrideLighting", "RiverSurface should mix in shared Stride lighting");
        AssertContains(shader, "float shadow = RiverStrideEvaluateSceneShadow(InputWorldSpacePos, 0.0f, lightDir, streams.DepthVS);", "RiverSurface should receive scene shadowing through the shared river shadow path");
        AssertContains(shader, "float3 waterSpecularColor = float3(_WaterSpecular, _WaterSpecular, _WaterSpecular);", "RiverSurface should build water specular color once before shared lighting");
        AssertContains(shader, "float3 waterColor = RiverStrideComputeLighting(", "RiverSurface should route water lighting through the shared Stride helper");
        AssertContains(shader, "waterDiffuse + foam", "RiverSurface shared lighting should include water diffuse and foam");
        AssertContains(shader, "waterSpecularColor", "RiverSurface shared lighting should use the prepared water specular color");
        AssertContains(shader, "fowGlossiness", "RiverSurface shared lighting should use the target glossiness");
        AssertContains(shader, "shadow, 1.0f", "RiverSurface shared lighting should use scene shadowing with full environment intensity");
        AssertContains(shader, "float3 lightDir = RiverStrideGetMainLightDirection();", "RiverSurface should use the shared main light direction helper");
        AssertContains(shader, "float3 lightColorNdotL = RiverStrideGetMainLightColorNdotL(waterNormal, shadow);", "RiverSurface should use the shared light color and NdotL helper");
        AssertContains(shader, "waterColor += RiverStrideComputeDirectSpecular(", "RiverSurface should add a direct-specular correction for the existing water specular factor");
        AssertContains(shader, "_WaterGlossScale * fowGlossiness", "RiverSurface direct-specular correction should preserve water gloss scaling");
        AssertContains(shader, "waterNormal, toCameraDir, lightDir, lightColorNdotL", "RiverSurface direct-specular correction should use shared light parameters");
        AssertContains(shader, "* (_WaterSpecularFactor - 1.0f);", "RiverSurface should preserve the existing water specular factor without changing diffuse energy");
        AssertNotContains(shader, "_DefaultEnvironmentSunDiffuse", "RiverSurface should stop declaring an independent sun color");
        AssertNotContains(shader, "_DefaultEnvironmentSunIntensity", "RiverSurface should stop declaring an independent sun intensity");
        AssertNotContains(shader, "_WaterToSunDir", "RiverSurface should stop declaring an independent sun direction");
        AssertNotContains(shader, "ImprovedBlinnPhong", "RiverSurface should not use its separate Blinn-Phong light model");
        AssertNotContains(shader, "ComposeLight", "RiverSurface should not use its separate light composition helper");
    }

    private static void RiverSceneLightingInputsBindToBottomAndSurface()
    {
        string loader = ReadRepositoryText("Terrain/Rendering/River/RiverResourceLoader.cs");
        string feature = ReadRepositoryText("Terrain/Rendering/River/RiverRenderFeature.cs");
        string viewportGame = ReadRepositoryText("Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs");

        AssertContains(feature, "PrepareRiverSceneLighting(context, renderView);", "RiverRenderFeature should prepare scene lighting for all river passes");
        AssertContains(feature, "bottomShadowMapRenderer = forwardLightingFeature?.ShadowMapRenderer;", "RiverRenderFeature should grab Stride's shadow-map renderer from forward lighting");
        AssertContains(feature, "var lightingView = renderView.LightingView ?? renderView;", "RiverRenderFeature should resolve river lighting from the current lighting view");
        AssertContains(feature, "LightDirectionalShadowMapRenderer.ShaderData", "RiverRenderFeature should read Stride's directional shadow shader data for river passes");
        AssertContains(feature, "sceneShadowDepthBias = shaderData.DepthBias;", "RiverRenderFeature should carry Stride's active shadow depth bias into the shared compare path");
        AssertContains(feature, "BindDirectionalLightToEffect(bottomEffect?.Parameters", "RiverRenderFeature should bind directional light data to RiverBottom");
        AssertContains(feature, "BindDirectionalLightToEffect(surfaceEffect?.Parameters", "RiverRenderFeature should bind directional light data to RiverSurface");
        AssertContains(feature, "BindEnvironmentToEffect(bottomEffect?.Parameters", "RiverRenderFeature should bind skybox data to RiverBottom");
        AssertContains(feature, "BindEnvironmentToEffect(surfaceEffect?.Parameters", "RiverRenderFeature should bind skybox data to RiverSurface");
        AssertContains(feature, "RiverStrideLightingKeys._SceneSunDirection", "RiverRenderFeature should use shared generated lighting keys");
        AssertContains(feature, "RiverStrideLightingKeys._SceneShadowDepthBias", "RiverRenderFeature should bind active scene shadow depth bias through shared lighting keys");
        AssertContains(feature, "RiverStrideLightingKeys._SceneWorldToShadowCascadeUV", "RiverRenderFeature should bind real scene world-to-shadow matrices through shared lighting keys");
        AssertContains(feature, "RiverStrideLightingKeys.SceneShadowMapTexture", "RiverRenderFeature should bind the shared shadow map key");
        AssertContains(feature, "SkyboxKeys.CubeMap", "RiverRenderFeature should read the real scene skybox specular cubemap");
        AssertContains(feature, "River bottom requires a real scene skybox cubemap.", "RiverRenderFeature should still reject non-target bottom environment fallbacks");
        AssertContains(feature, "RiverStrideLightingKeys.EnvironmentMapTexture", "RiverRenderFeature should bind the shared environment map key");
        AssertContains(feature, "RiverStrideLightingKeys._EnvironmentSkyMatrix", "RiverRenderFeature should bind scene skybox rotation through shared lighting keys");
        AssertContains(feature, "RiverStrideLightingKeys._EnvironmentIntensity", "RiverRenderFeature should bind scene skybox intensity through shared lighting keys");
        AssertContains(feature, "surfaceEffect.Parameters.Set(RiverSurfaceKeys.WaterColorSampler, graphicsDevice.SamplerStates.LinearWrap);", "RiverRenderFeature should bind a dedicated water-color sampler");
        AssertContains(feature, "SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.ReflectionSpecularTexture, riverResources.ReflectionSpecular);", "RiverRenderFeature should keep the river reflection/specular asset on the surface pass");
        AssertContains(viewportGame, "_riverComponent.Settings.BottomEnvironmentIntensity = 1.0f;", "EmbeddedStrideViewportGame should keep only bottom environment tuning as an explicit river-side multiplier");
        AssertNotContains(feature, "RiverBottomEffectKeys.BottomLightGroup", "RiverRenderFeature should not depend on a brittle light-group permutation");
        AssertNotContains(feature, "RiverBottomKeys._ShadowTermFallback", "RiverRenderFeature should not bind the old river-local shadow fallback into the bottom pass");
        AssertNotContains(feature, "RiverBottomKeys._SceneSunDirection", "Scene lighting should no longer be bottom-only");
        AssertNotContains(feature, "RiverBottomKeys._SceneSunColor", "Scene sun color should no longer be bottom-only");
        AssertNotContains(feature, "RiverBottomKeys._SceneShadowDepthBias", "Scene shadow depth bias should no longer be bottom-only");
        AssertNotContains(feature, "RiverBottomKeys._SceneWorldToShadowCascadeUV", "Scene shadow matrices should no longer be bottom-only");
        AssertNotContains(feature, "RiverBottomKeys.SceneShadowMapTexture", "Scene shadow map should no longer be bottom-only");
        AssertNotContains(feature, "RiverBottomKeys.EnvironmentMapTexture", "Scene environment should no longer be bottom-only");
        AssertNotContains(feature, "RiverBottomKeys._EnvironmentSkyMatrix", "Scene skybox rotation should no longer be bottom-only");
        AssertNotContains(feature, "RiverBottomKeys._EnvironmentIntensity", "Scene skybox intensity should no longer be bottom-only");
        AssertNotContains(loader, "BottomEnvironmentUrl", "RiverResourceLoader should not keep a bottom environment fallback URL once river IBL is scene-only");
        AssertNotContains(loader, "EnsureBottomEnvironment", "RiverResourceLoader should not keep a bottom environment fallback loader once river IBL is scene-only");
        AssertNotContains(feature, "riverResources.EnsureBottomEnvironment", "RiverRenderFeature should not fall back to an optional skybox texture for river IBL");
        AssertNotContains(feature, "bottomEnvironment ??= riverResources.ReflectionSpecular", "RiverRenderFeature should not use the surface reflection fallback cubemap for river IBL");
        AssertNotContains(viewportGame, "_riverComponent.Settings.BottomSunIntensity =", "EmbeddedStrideViewportGame should no longer override the scene directional-light intensity for river bottom");
    }

    private static void SharedLightingShaderIsRegisteredForStrideKeyGeneration()
    {
        string project = ReadRepositoryText("Terrain/Terrain.csproj");
        string package = ReadRepositoryText("Terrain/Terrain.sdpkg");

        AssertContains(package, "!dir Effects", "Terrain.sdpkg should include the Effects folder that contains RiverStrideLighting.sdsl");
        AssertContains(project, "<Compile Update=\"Effects\\River\\RiverStrideLighting.sdsl.cs\">", "Terrain.csproj should compile generated RiverStrideLighting shader keys");
        AssertContains(project, "<None Update=\"Effects\\River\\RiverStrideLighting.sdsl\">", "Terrain.csproj should register RiverStrideLighting for shader key generation metadata");
        AssertContains(project, "<LastGenOutput>RiverStrideLighting.sdsl.cs</LastGenOutput>", "Terrain.csproj should name the generated RiverStrideLighting key file");
    }

    private static void BottomShaderDoesNotApplyGlobalLightingEnergyBoost()
    {
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverBottom.sdsl");

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
        string renderObject = ReadRepositoryText("Terrain/Rendering/River/RiverRenderObject.cs");
        string meshData = ReadRepositoryText("Terrain/Rendering/River/RiverMeshData.cs");
        string meshService = ReadRepositoryText("Terrain/Rivers/RiverMeshService.cs");
        string settings = ReadRepositoryText("Terrain/Rendering/River/RiverRenderSettings.cs");
        string processor = ReadRepositoryText("Terrain/Rendering/River/RiverProcessor.cs");
        string feature = ReadRepositoryText("Terrain/Rendering/River/RiverRenderFeature.cs");

        AssertContains(settings, "public float FlattenMultiplier { get; set; } = 1.0f;", "RiverRenderSettings should expose water normal flattening");
        AssertContains(settings, "public float BottomNormalStrength { get; set; } = 1.0f;", "RiverRenderSettings should expose bottom normal strength for bottom lighting");
        AssertContains(settings, "public float BottomEnvironmentIntensity { get; set; }", "RiverRenderSettings should expose bottom environment intensity");
        AssertContains(settings, "public float RiverMaxVisibleCameraHeight { get; set; } = 3000.0f;", "RiverRenderSettings should expose the camera-height river visibility cutoff");
        AssertContains(settings, "public float SeaLevel { get; set; } = 3.8f;", "RiverRenderSettings should expose runtime map sea level for river water height");
        AssertNotContains(renderObject, "ApplySettings", "RiverRenderObject should not copy shared RiverRenderSettings per object every frame");
        AssertNotContains(renderObject, "settings.", "RiverRenderObject should not cache pass-wide RiverRenderSettings fields");
        AssertContains(renderObject, "public Vector2 MapWorldSize { get; private set; } = new(4096.0f, 4096.0f);", "RiverRenderObject should cache per-axis map world size for rectangular map UV normalization");
        AssertContains(meshData, "public float RefractionMaxCameraHeight { get; init; } = 50.0f;", "River mesh data should carry the height-scale-aware refraction clamp plane");
        AssertContains(meshData, "RefractionMaxCameraHeight = RefractionMaxCameraHeight,", "River mesh snapshots should preserve the refraction clamp plane");
        AssertContains(meshService, "float refractionMaxCameraHeight = float.IsFinite(boundingBox.Maximum.Y)", "River mesh generation should derive the refraction clamp plane from actual generated river height");
        AssertContains(meshService, "? MathF.Ceiling(boundingBox.Maximum.Y + 1.0f)", "River mesh generation should leave a small clearance above the highest river vertex");
        AssertContains(meshService, "RefractionMaxCameraHeight = refractionMaxCameraHeight,", "River mesh data should carry the generated-height refraction clamp plane");
        AssertContains(renderObject, "public float RefractionMaxCameraHeight { get; private set; } = 50.0f;", "RiverRenderObject should cache the refraction clamp plane");
        AssertContains(renderObject, "RefractionMaxCameraHeight = mesh.RefractionMaxCameraHeight;", "RiverRenderObject should keep the refraction clamp plane from mesh data");
        AssertContains(renderObject, "MapWorldSize = mesh.MapWorldSize;", "RiverRenderObject should keep the per-axis map world size from the generated river mesh");
        AssertNotContains(processor, "renderObject.ApplySettings(component.Settings);", "RiverProcessor should not push pass-wide settings into every render object each frame");
        AssertContains(processor, "renderObject.World = entity.Transform.WorldMatrix;", "RiverProcessor should still update per-object transform state each frame");
        AssertContains(processor, "component.Settings.RiverMaxVisibleCameraHeight = bundle.RiverMaxVisibleCameraHeight;", "RiverProcessor should copy runtime TOML camera-height cutoff into render settings");
        AssertContains(processor, "component.Settings.SeaLevel = bundle.SeaLevel;", "RiverProcessor should copy runtime TOML sea level into render settings");
        AssertContains(feature, "private static RiverRenderSettings? GetRiverSettings(RiverRenderObject riverObject)", "RiverRenderFeature should read pass-wide settings from the render object's source component");
        AssertContains(feature, "return (riverObject.Source as RiverComponent)?.Settings;", "RiverRenderFeature should not depend on duplicated RiverRenderObject settings");
        AssertContains(feature, "float riverMaxVisibleCameraHeight = ResolveRiverMaxVisibleCameraHeight(renderViewStage, startIndex, endIndex);", "RiverRenderFeature should resolve the camera-height cutoff before river pass work");
        AssertContains(feature, "if (cameraWorldPosition.Y >= riverMaxVisibleCameraHeight)", "RiverRenderFeature should skip river rendering when the camera is at or above the cutoff");
        AssertContains(feature, "Texture? sceneColor = commandList.RenderTargetCount > 0 ? commandList.RenderTargets[0] : null;", "RiverRenderFeature should resolve the actual scene color before allocating river refraction targets");
        AssertContains(feature, "renderResources.EnsureResources(graphicsDevice, sceneColor.ViewWidth, sceneColor.ViewHeight);", "RiverRenderFeature should size river refraction targets from the actual scene color source");
        AssertContains(feature, "private float ResolveRiverMaxVisibleCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)", "RiverRenderFeature should keep camera-height cutoff resolution explicit");
        AssertContains(feature, "maxVisibleCameraHeight = foundRiverObject", "RiverRenderFeature should resolve a stable draw-range camera-height cutoff instead of depending on sorted node order");
        AssertContains(feature, "? MathF.Max(maxVisibleCameraHeight, riverMaxVisibleCameraHeight)", "RiverRenderFeature should use the highest river camera-height cutoff across the draw range");
        AssertNotContains(feature, "bottomEffect.Parameters.Set(RiverBottomKeys._WaterHeight, 3.0f);", "RiverRenderFeature should not bind a static water height for bottom ocean fade");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._WaterHeight, settings.SeaLevel);", "RiverRenderFeature should bind bottom water height from shared river settings");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._WaterHeight, settings.SeaLevel);", "RiverRenderFeature should bind surface water height from shared river settings");
        AssertContains(feature, "&& sourceSettings.SeaLevel == candidateSettings.SeaLevel", "RiverRenderFeature should include sea level in the shared-parameter invariant");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._TextureUvScale, settings.TextureUvScale);", "RiverRenderFeature should continue binding texture UV scale for available shader variants");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._OceanFadeRate, settings.OceanFadeRate);", "RiverRenderFeature should bind bottom ocean fade rate from shared settings");
        AssertContains(feature, "bottomEffect.Parameters.Set(RiverBottomKeys._WorldToMapUnitScale, 0.5f);", "RiverRenderFeature should bind the static local world-to-map-unit conversion for world-UV bottom sampling");
        AssertContains(feature, "BindRefractionMaxCameraHeight(bottomEffect, refractionMaxCameraHeight);", "RiverRenderFeature should bind the draw-range refraction clamp plane into bottom");
        AssertContains(feature, "effect.Parameters.Set(RiverBottomKeys._BottomNormalStrength, settings.BottomNormalStrength);", "RiverRenderFeature should bind bottom normal strength from shared settings");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._ViewMatrix, viewMatrix);", "RiverRenderFeature should bind the view matrix required by view-space refraction offset");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._GlobalTime, globalTime);", "RiverRenderFeature should bind animated water time");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._FlattenMult, settings.FlattenMultiplier);", "RiverRenderFeature should bind water normal flattening");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._MapWorldSize, riverObject.MapWorldSize);", "RiverRenderFeature should bind per-axis map world size into the surface shader");
        AssertContains(feature, "BindRefractionMaxCameraHeight(surfaceEffect, refractionMaxCameraHeight);", "RiverRenderFeature should bind the same draw-range refraction clamp plane into surface");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._BankFade, settings.BankFade);", "RiverRenderFeature should bind the target river bank fade into the surface shader");
        AssertContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionScale, settings.WaterRefractionScale);", "RiverRenderFeature should bind refraction scale from shared settings");
        AssertContains(feature, "public override void Prepare(RenderDrawContext context)", "RiverRenderFeature should move non-frame river parameter binding out of Draw");
        AssertContains(feature, "ApplyBottomParameters(bottomEffect, riverParametersSource, riverSettings);", "RiverRenderFeature should bind bottom parameters once during Prepare");
        AssertContains(feature, "ApplySurfaceParameters(surfaceEffect, riverParametersSource, riverSettings);", "RiverRenderFeature should bind surface parameters once during Prepare");
        AssertContains(feature, "DebugAssertPreparedRiverParametersMatch(riverParametersSource, riverSettings);", "RiverRenderFeature should assert the shared non-frame river parameter invariant");
        AssertContains(feature, "RiverParametersMatch(source, sourceSettings, riverObject, candidateSettings)", "RiverRenderFeature should compare prepared river parameters before sharing one binding");
        AssertNotContains(feature, "ApplyBottomParameters(bottomEffect, bottomParametersSource);", "RiverRenderFeature should not bind full bottom parameters in Draw");
        AssertNotContains(feature, "ApplySurfaceParameters(surfaceEffect, surfaceParametersSource);", "RiverRenderFeature should not bind full surface parameters in Draw");
        AssertNotContains(feature, "ApplySurfaceParameters(effect, riverObject);", "RiverRenderFeature should not unconditionally bind all surface parameters inside the per-object draw loop");
        AssertNotContains(feature, "ApplyBottomParameters(effect, riverObject);", "RiverRenderFeature should not unconditionally bind all bottom parameters inside the per-object draw loop");
        AssertNotContains(feature, "effect.Parameters.Set(RiverCommonKeys._RefractionMaxCameraHeight, riverObject.RefractionMaxCameraHeight);", "RiverRenderFeature should not bind object-local refraction clamp while scene seed uses a draw-range clamp");
        AssertNotContains(feature, "_BottomSpecularIntensity", "RiverRenderFeature should not bind river-local bottom specular intensity");
    }

    private static void SurfaceShaderSamplesWaterTextureAssets()
    {
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverSurface.sdsl");

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
        AssertContains(shader, "WaterColorTexture.Sample(WaterColorSampler", "RiverSurface should sample the water-color texture through its dedicated sampler");
        AssertNotContains(shader, "_WaterColorSurfaceLift", "RiverSurface should not keep the temporary water-color lift once the post wrapper is restored");
        AssertNotContains(shader, "visibleWaterColor", "RiverSurface should not keep the temporary water-color floor once the post wrapper is restored");
        AssertNotContains(shader, "DecodeWaterColorSrgb", "RiverSurface should not manually decode water-color RGB because water_color.dds is loaded as a UNorm texture");
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
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverSurface.sdsl");

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
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverSurface.sdsl");

        AssertContains(shader, "float2 ComputeMapWorldUv(float3 worldPosition)", "RiverSurface should centralize map-space UV conversion");
        AssertContains(shader, "GetMapUnitXZ(worldPosition) / max(_MapWorldSize, float2(1.0f, 1.0f))", "RiverSurface should normalize map-unit UVs per axis");
        AssertContains(shader, "uv.y = 1.0f - uv.y;", "RiverSurface should flip map-space Y before sampling water-color maps");
        AssertContains(shader, "float CalcRiverProfileDepth(float2 riverUv)", "RiverSurface should use a local non-advanced river profile depth helper");
        AssertContains(shader, "return _Depth * (1.0f - pow(cos(riverUv.y * 2.0f * 3.14159265f) * 0.5f + 0.5f, 2.0f));", "RiverSurface should use the target cosine depth profile");
        AssertContains(shader, "float3 CalcRefraction(float3 WorldSpacePos, float3 Normal, float2 ScreenPos, float3 WaterColor, float Depth)", "RiverSurface should keep refraction as a dedicated water function");
        AssertContains(shader, "float4 refractionSample = RefractionTexture.Sample(RefractionSampler, screenUv);", "RiverSurface should first sample the undistorted refraction buffer");
        AssertContains(shader, "stage float2 _RefractionTextureSize = float2(1.0f, 1.0f);", "RiverSurface should know the refraction texture size for unfiltered payload reads");
        AssertContains(shader, "float SampleRefractionPayload(float2 screenUv)", "RiverSurface should separate refraction color filtering from depth payload reads");
        AssertContains(shader, "return RefractionTexture.Load(int3(ComputeRefractionPayloadCoord(screenUv), 0)).a;", "RiverSurface should read the distance payload without linear filtering");
        AssertContains(shader, "float refractionPayload = SampleRefractionPayload(screenUv);", "RiverSurface should read the base refraction payload with point/Load semantics");
        AssertContains(shader, "float3 refractionWorldPosition = DecodeRefractionWorldPosition(WorldSpacePos, refractionPayload);", "RiverSurface should decode the base refraction world position from the unfiltered payload");
        AssertContains(shader, "Depth = min(Depth, refractionDepth);", "RiverSurface should use the shallower depth for the refraction shore mask");
        AssertContains(shader, "float4 offsetRefractionSample = RefractionTexture.Sample(RefractionSampler, screenUv + refractionOffset);", "RiverSurface should sample offset refraction separately");
        AssertContains(shader, "float offsetRefractionPayload = SampleRefractionPayload(screenUv + refractionOffset);", "RiverSurface should read the offset refraction payload with point/Load semantics");
        AssertContains(shader, "float3 offsetRefractionWorldPosition = DecodeRefractionWorldPosition(WorldSpacePos, offsetRefractionPayload);", "RiverSurface should decode the offset refraction world position from the unfiltered payload");
        AssertContains(shader, "float offsetStep = step(WorldSpacePos.y, offsetRefractionWorldPosition.y);", "RiverSurface should reject offset samples above the water surface");
        AssertContains(shader, "float seeThroughDistance = refractionDepth * _WorldToMapUnitScale;", "RiverSurface see-through attenuation should use map-unit distance so local Stride world scaling does not hide the bottom");
        AssertContains(shader, "return CalcTerrainUnderwaterSeeThrough(refractionDepth, refractionWorldPosition, refractionWaterColorMap, refractionSample.rgb);", "RiverSurface see-through should use the target RefractionDepth path");
        AssertContains(shader, "float waterFade = ComputeWaterFade(depth);", "RiverSurface WaterFade should use the separate base-refraction depth path");
        AssertContains(shader, "float3 refractionColor = CalcRefraction(InputWorldSpacePos, waterNormal, InputScreenSpacePos.xy, waterColorAndSpec.rgb, InputDepth);", "RiverSurface should route refraction through CalcRefraction");
        AssertContains(shader, "float worldDepth = depth * worldWidth + 0.1f;", "RiverSurface should pass world-scaled profile depth into CalcWater like the target advanced branch");
        AssertContains(shader, "waterColor.a = ComputeAdvancedSurfaceAlpha(riverUv, transparency, connectionFade);", "RiverSurface final alpha should follow the target advanced branch");
        AssertNotContains(shader, "saturate(depth * 2.0f / max(_Depth, 0.0001f)) * transparency * connectionFade", "RiverSurface should not use the non-advanced CalcRiverSurface alpha path");
        AssertNotContains(shader, "SampleRefractionSeeThrough", "RiverSurface should not keep the old combined refraction helper");
        AssertNotContains(shader, "ComputeRiverWaterFade", "RiverSurface should not keep the old water-fade adapter");
        AssertNotContains(shader, "_DepthFactor", "RiverSurface should not pass a cross-section depth factor into water shading");
        AssertNotContains(shader, "seeThroughDepth", "RiverSurface should not cap see-through attenuation by river profile depth");
        AssertNotContains(shader, "effectiveDepth", "RiverSurface should not reintroduce the old cross-section adapter naming");
        AssertNotContains(shader, "RiverDepthFromCrossSection(riverUv.y", "RiverSurface should not use the bank-width power helper for this path");
        AssertNotContains(shader, "DecodeRefractionWorldPosition(WorldSpacePos, refractionSample.a)", "RiverSurface should not linearly filter the base refraction distance payload");
        AssertNotContains(shader, "DecodeRefractionWorldPosition(WorldSpacePos, offsetRefractionSample.a)", "RiverSurface should not linearly filter the offset refraction distance payload");
    }

    private static void SurfaceShaderUsesTargetAdvancedAlphaBranch()
    {
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverSurface.sdsl");

        AssertContains(shader, "float ComputeAdvancedSurfaceAlpha(float2 riverUv, float transparency, float connectionFade)", "RiverSurface should centralize the target advanced alpha branch");
        AssertContains(shader, "float edgeFade1 = smoothstep(0.0f, max(_BankFade, 0.0001f), riverUv.y);", "RiverSurface advanced alpha should use the shared river bank fade on the first edge");
        AssertContains(shader, "float edgeFade2 = smoothstep(0.0f, max(_BankFade, 0.0001f), 1.0f - riverUv.y);", "RiverSurface advanced alpha should use the shared river bank fade on the second edge");
        AssertContains(shader, "return transparency * connectionFade * edgeFade1 * edgeFade2;", "RiverSurface advanced alpha should match CalcRiverAdvanced after JOMINI_REFRACTION_ENABLED");
        AssertContains(shader, "waterColor.a = ComputeAdvancedSurfaceAlpha(riverUv, transparency, connectionFade);", "RiverSurface should write the target advanced alpha branch");
        AssertNotContains(shader, "_SurfaceBankFade", "RiverSurface should not keep the previous local bank-fade workaround");
        AssertNotContains(shader, "ComputeSurfaceAlpha", "RiverSurface should not keep the previous depth-alpha workaround");
    }

    private static void SurfaceShaderDoesNotApplyRemovedPostWrapper()
    {
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverSurface.sdsl");
        string feature = ReadRepositoryText("Terrain/Rendering/River/RiverRenderFeature.cs");
        string loader = ReadRepositoryText("Terrain/Rendering/River/RiverResourceLoader.cs");

        AssertContains(shader, "CalcRiverAdvanced(waterColor);", "RiverSurface PS should still compute the target water body");
        AssertContains(shader, "streams.ColorTarget = waterColor;", "RiverSurface PS should write CalcRiverAdvanced output directly");
        AssertNotContains(shader, "ApplySurfacePostProcessing", "RiverSurface should not keep the removed post wrapper");
        AssertNotContains(shader, "HeightmapSlice0", "RiverSurface should not declare editor terrain height slices for removed terrain shadow tint");
        AssertNotContains(shader, "ShadowNoiseTexture", "RiverSurface should not declare shadow tint textures after removing the wrapper");
        AssertNotContains(shader, "CalculateRiverTerrainNormal", "RiverSurface should not calculate terrain normals after removing terrain shadow tint");
        AssertNotContains(shader, "_MapSadowTint", "RiverSurface should not keep shadow tint parameters after removing the wrapper");
        AssertNotContains(shader, "_FogBegin2", "RiverSurface should not keep wrapper distance-fog parameters");
        AssertNotContains(shader, "_FogEnd2", "RiverSurface should not keep wrapper distance-fog parameters");
        AssertNotContains(shader, "ApplyOvercastContrast", "RiverSurface should not keep the wrapper overcast contrast helper");
        AssertNotContains(shader, "ApplyTerrainShadowTintWithClouds", "RiverSurface should not keep terrain shadow tint after CalcRiverAdvanced");
        AssertNotContains(shader, "ApplyMapDistanceFogWithoutFoW", "RiverSurface should not keep wrapper map distance fog");
        AssertNotContains(shader, "_InverseWorldSize", "RiverSurface should not expose procedural cloud-shadow world-size inputs");
        AssertNotContains(shader, "_HasCloudShadowEnabled", "RiverSurface should not expose the removed procedural cloud shadow toggle");
        AssertNotContains(shader, "GetCloudShadowMask", "RiverSurface should not keep the time-varying procedural cloud shadow helper");
        AssertNotContains(shader, "color.a *= 1.0f - _FlatMapLerp;", "RiverSurface should not keep wrapper flat-map alpha fade");
        AssertNotContains(shader, "color.a *= zoomBlendOut;", "RiverSurface should not keep wrapper zoom alpha fade");
        AssertNotContains(shader, "ShadowNoiseSampler", "RiverSurface should not keep the removed shadow tint sampler");
        AssertNotContains(shader, "TerrainHeightSampler", "RiverSurface should not keep the removed terrain height sampler");
        AssertNotContains(shader, "float terrainShadow = saturate((1.0f - terrainNormal.y) * 1.35f);", "RiverSurface should not keep the simplified post-restore normal-y tint approximation");
        AssertNotContains(shader, "float fogFactor = smoothstep(4500.0f, 11000.0f, cameraDistance);", "RiverSurface should not keep the simplified post-restore distance fog approximation");
        AssertNotContains(shader, "_ShadowTermFallback", "RiverSurface should not keep the old shadow fallback parameter");
        AssertNotContains(shader, "_CloudMaskFallback", "RiverSurface should not keep the old cloud fallback parameter");
        AssertNotContains(shader, "_ZoomBlendOut", "RiverSurface should derive zoom fade from the target water zoom factor");
        AssertNotContains(shader, "FogOfWarAlphaTexture", "RiverSurface should not depend on a strategy-layer fog-of-war texture for river shading");
        AssertNotContains(shader, "FogOfWarAlphaSampler", "RiverSurface should not bind a strategy-layer fog-of-war sampler");
        AssertNotContains(shader, "_HasFogOfWarAlphaTexture", "RiverSurface should not keep a fog-of-war capability switch");
        AssertNotContains(shader, "ApplyFogOfWar", "RiverSurface should not run strategy-layer fog-of-war color adjustment");
        AssertNotContains(shader, "SampleFogOfWarAlphaRaw", "RiverSurface should not sample strategy-layer fog-of-war alpha");

        AssertNotContains(loader, "ShadowColorFileName", "RiverResourceLoader should not require shadow_color.dds after removing the wrapper");
        AssertNotContains(loader, "ShadowColor", "RiverResourceLoader should not keep the removed shadow tint texture property");
        AssertNotContains(feature, "TryBindEditorTerrainInputs()", "RiverRenderFeature should not bind editor terrain height slices into the surface pass");
        AssertNotContains(feature, "RiverSurfaceKeys.ShadowNoiseTexture", "RiverRenderFeature should not bind the removed shadow tint texture");
        AssertNotContains(feature, "RiverSurfaceKeys.HeightmapSlice0", "RiverRenderFeature should not bind editor terrain height slices into river surface");
        AssertNotContains(feature, "RiverSurfaceKeys._InverseWorldSize", "RiverRenderFeature should not bind procedural cloud-shadow world-size inputs");
        AssertNotContains(feature, "RiverSurfaceKeys._HasCloudShadowEnabled", "RiverRenderFeature should not bind the removed procedural cloud-shadow toggle");
        AssertNotContains(feature, "RiverSurfaceKeys.TerrainHeightSampler", "RiverRenderFeature should not bind terrain height samplers into the surface pass");
        AssertNotContains(feature, "RiverSurfaceKeys.ShadowNoiseSampler", "RiverRenderFeature should not bind removed shadow tint samplers into the surface pass");
        AssertNotContains(feature, "SurfaceFogOfWarAlphaTexture", "RiverRenderFeature should not expose fog-of-war as a river surface input");
        AssertNotContains(feature, "FogOfWarAlphaTexture", "RiverRenderFeature should not bind fog-of-war textures into the river surface pass");
        AssertNotContains(feature, "FogOfWarAlphaSampler", "RiverRenderFeature should not bind a fog-of-war sampler into the river surface pass");
        AssertNotContains(feature, "_HasFogOfWarAlphaTexture", "RiverRenderFeature should not keep a fog-of-war capability switch");
        AssertNotContains(feature, "VisibleFogAlpha", "RiverRenderFeature should not create a white fog-of-war substitute");
        AssertNotContains(feature, "FlatHeightmap", "RiverRenderFeature should not create a flat terrain-normal substitute");
    }

    private static void RenderFeatureSeparatesSceneSeedFromWorkingBuffer()
    {
        string resources = ReadRepositoryText("Terrain/Rendering/River/RiverRenderResources.cs");
        string feature = ReadRepositoryText("Terrain/Rendering/River/RiverRenderFeature.cs");

        AssertContains(resources, "public Texture? SceneSeedColor { get; private set; }", "RiverRenderResources should allocate a dedicated scene seed buffer");
        AssertContains(feature, "renderResources.SceneSeedColor", "RiverRenderFeature should use the dedicated scene seed texture");
        AssertContains(feature, "seedEffect.SetOutput(seedTarget);", "RiverRenderFeature should seed scene color into SceneSeedColor first");
        AssertContains(feature, "commandList.CopyRegion(renderResources.SceneSeedColor, 0, null, renderResources.BottomColor, 0);", "RiverRenderFeature should copy the scene seed into the working bottom/refraction buffer before the bottom pass");
        AssertContains(feature, "surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);", "RiverRenderFeature should sample refraction with the target linear clamp sampler");
        AssertContains(feature, "surfaceEffect.Parameters.Set(RiverSurfaceKeys._RefractionTextureSize, new Vector2(renderResources.Width, renderResources.Height));", "RiverRenderFeature should bind the working refraction size for unfiltered payload reads");
        AssertNotContains(feature, "Texture? refractionTexture)", "RiverRenderFeature should not pass the shared refraction texture into the per-object draw loop");
        AssertNotContains(feature, "effect.Parameters.Set(RiverSurfaceKeys.RefractionTexture, refractionTexture);", "RiverRenderFeature should not bind the shared refraction texture per river object");
        AssertNotContains(feature, "effect.Parameters.Set(RiverSurfaceKeys._RefractionTextureSize, new Vector2(refractionTexture.Width, refractionTexture.Height));", "RiverRenderFeature should not refresh the shared refraction size per river object");
        AssertContains(feature, "blendState.RenderTargets[0].AlphaSourceBlend = Blend.SecondarySourceAlpha;", "RiverRenderFeature should match the target bottom pass alpha source factor");
        AssertContains(feature, "blendState.RenderTargets[0].AlphaDestinationBlend = Blend.InverseSecondarySourceAlpha;", "RiverRenderFeature should match the target bottom pass alpha destination factor");
        AssertNotContains(feature, "RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.PointClamp", "RiverRenderFeature should not keep the previous point-clamp refraction sampler binding");
    }

    private static void RenderFeatureSceneSeedWritesDepthPayload()
    {
        string feature = ReadRepositoryText("Terrain/Rendering/River/RiverRenderFeature.cs");
        string shader = ReadRepositoryText("Terrain/Effects/River/RiverSceneSeed.sdsl");
        string project = ReadRepositoryText("Terrain/Terrain.csproj");

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
        AssertContains(shader, "shader RiverSceneSeed : ImageEffectShader, DepthBase, Transformation, RiverCommon", "RiverSceneSeed should share the refraction distance packing helper");
        AssertContains(shader, "RiverCompressWorldSpace(positionWS.xyz, Eye.xyz)", "RiverSceneSeed alpha should match the height-scale-aware refraction distance semantics");
        AssertContains(feature, "seedEffect.Parameters.Set(RiverCommonKeys._RefractionMaxCameraHeight, refractionMaxCameraHeight);", "RiverRenderFeature should bind the same refraction clamp plane into RiverSceneSeed");
        AssertContains(shader, "return float4(seedColor, sceneDistance)", "RiverSceneSeed should preserve RGB and write scene distance payload alpha");
        AssertContains(project, "<Compile Update=\"Effects\\River\\RiverSceneSeed.sdsl.cs\">", "Terrain.csproj should compile the generated RiverSceneSeed shader key file");
        AssertNotContains(feature, "refractionSeedScaler.Color = new Color4(1.0f, 1.0f, 1.0f, 0.0f);", "River scene seed should not clear alpha to zero because the seed carries scene distance payload");
        AssertNotContains(feature, "new ImageScaler(SamplingPattern.Linear, delaySetRenderTargets: true)", "RiverRenderFeature should not use ImageScaler for river seed once depth payload is required");
    }

    private static void BottomShaderPacksRefractionDistanceForSurfaceSeeThrough()
    {
        string common = ReadRepositoryText("Terrain/Effects/River/RiverCommon.sdsl");
        string bottom = ReadRepositoryText("Terrain/Effects/River/RiverBottom.sdsl");
        string surface = ReadRepositoryText("Terrain/Effects/River/RiverSurface.sdsl");

        AssertContains(common, "float RiverCompressWorldSpace(float3 worldPosition, float3 cameraPosition)", "RiverCommon should pack camera-relative distance");
        AssertContains(common, "float3 RiverDecompressWorldSpace(float3 surfaceWorldPosition, float compressedDistance, float3 cameraPosition)", "RiverCommon should provide the matching surface-side decompression");
        AssertContains(common, "stage float _RefractionMaxCameraHeight = 50.0f;", "RiverCommon should default to the target refraction clamp plane");
        AssertContains(common, "float maxHeight = max(_RefractionMaxCameraHeight, 50.0f);", "RiverCommon should allow large terrain height scales to raise the refraction clamp plane");
        AssertNotContains(common, "const float maxHeight = 50.0f;", "RiverCommon should not hard-code the low-height refraction clamp plane");
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
        string loader = ReadRepositoryText("Terrain/Rendering/River/RiverResourceLoader.cs");

        AssertContains(loader, "LoadRequiredLocalTexture", "RiverResourceLoader should have an explicit local file loading path for Bottom/Water textures");
        AssertContains(loader, "game/map/water", "RiverResourceLoader should report the local river texture directory when a file is missing");
        AssertContains(loader, "Log.Error($\"River local texture file '{path}' is missing from game/map/water.\");", "RiverResourceLoader should log missing local texture files before failing");
        AssertContains(loader, "File.OpenRead(path)", "RiverResourceLoader should let missing local files fail naturally after logging");
        AssertContains(loader, "Texture.Load(", "RiverResourceLoader should create GPU textures directly from DDS streams");
        AssertContains(loader, "WaterColor = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, WaterColorFileName, loadAsSrgb: false);", "RiverResourceLoader should load water-color DDS as a UNorm texture");
        AssertContains(loader, "ReflectionSpecularUrl = \"River/Environment/reflection-specular\"", "RiverResourceLoader should keep the environment reflection cubemap as a Stride content asset");
        AssertNotContains(loader, "ShadowColorFileName", "RiverResourceLoader should not require removed shadow tint texture files");
        AssertNotContains(loader, "ShadowColor = LoadRequiredLocalTexture", "RiverResourceLoader should not load removed shadow tint textures");
        AssertNotContains(loader, "catch (Exception", "RiverResourceLoader should not catch texture load exceptions; load failures should crash with their original exception");
        AssertNotContains(loader, "River/Water/", "RiverResourceLoader should not keep Stride content URLs for water textures");
        AssertNotContains(loader, "River/Bottom/", "RiverResourceLoader should not keep Stride content URLs for bottom textures");
    }

    private static string ReadRepositoryText(string relativePath)
    {
        return File.ReadAllText(GetRepositoryPath(relativePath));
    }

    private static string GetRepositoryPath(string relativePath)
    {
        return Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static float VisibilitySmithSchlickGgx(float alphaRoughness, float nDotL, float nDotV)
    {
        float k = alphaRoughness * 0.5f;
        float visibilityL = nDotL / (nDotL * (1.0f - k) + k);
        float visibilityV = nDotV / (nDotV * (1.0f - k) + k);
        return visibilityL * visibilityV / (nDotL * nDotV);
    }

    private static (float Scale, float Bias) DfgPolynomial(float specularY, float alphaRoughness, float nDotV)
    {
        float x = 1.0f - alphaRoughness;
        float y = nDotV;

        float bias = Saturate(MathF.Min(
            -0.1688f * x + 1.895f * x * x,
            0.9903f - 4.853f * y + 8.404f * y * y - 5.069f * y * y * y));

        float delta = Saturate(
            0.6045f
            + 1.699f * x
            - 0.5228f * y
            - 3.603f * x * x
            + 1.404f * x * y
            + 0.1939f * y * y
            + 2.661f * x * x * x);

        float scale = delta - bias;
        bias *= Saturate(50.0f * specularY);
        return (scale, bias);
    }

    private static float Saturate(float value)
    {
        return Math.Clamp(value, 0.0f, 1.0f);
    }

    private static void AssertNearlyEqual(float expected, float actual, float tolerance, string message)
    {
        TestHarness.Assert(MathF.Abs(expected - actual) <= tolerance, $"{message}. Expected: {expected}. Actual: {actual}.");
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
