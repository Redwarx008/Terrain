#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Images;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using Stride.Rendering.Skyboxes;

namespace Terrain.Rendering.River;

public enum RiverRenderDebugMode
{
    Normal,
    Wireframe,
    BottomOnly,
    SurfaceOnly,
}

public sealed class RiverRenderFeature : RootRenderFeature
{
    private const int BottomDepthBias = -1;
    private const float BottomSlopeScaleDepthBias = -1.0f;
    private const int SurfaceDepthBias = -50000;
    private const float TargetSurfaceDepthBiasNearClip = 10.0f;
    private const float SurfaceDepthBiasNearClipScaleExponent = 0.5f;
    private const float SurfaceSlopeScaleDepthBias = 0.0f;

    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");
    private static readonly InputElementDescription[] RiverInputElements = RiverVertex.Layout.CreateInputElements();
    private static readonly FieldInfo? RenderViewDatasField = typeof(ForwardLightingRenderFeature).GetField("renderViewDatas", BindingFlags.Instance | BindingFlags.NonPublic);
    private DynamicEffectInstance? bottomEffect;
    private DynamicEffectInstance? surfaceEffect;
    private MutablePipelineState? bottomPipelineState;
    private MutablePipelineState? surfacePipelineState;
    private ImageEffectShader? sceneSeedEffect;
    private readonly RiverRenderResources renderResources = new();
    private readonly RiverResourceLoader riverResources = new();
    private readonly List<RenderLight> fallbackVisibleLights = new(8);
    private ContentManager? contentManager;
    private ForwardLightingRenderFeature? forwardLightingFeature;
    private IShadowMapRenderer? bottomShadowMapRenderer;

    public RiverRenderDebugMode DebugMode { get; set; }

    public override Type SupportedRenderObjectType => typeof(RiverRenderObject);

    public RiverRenderFeature()
    {
        SortKey = 190;
    }

    protected override void InitializeCore()
    {
        base.InitializeCore();

        bottomEffect = new DynamicEffectInstance("RiverBottom");
        bottomEffect.Initialize(Context.Services);
        surfaceEffect = new DynamicEffectInstance("RiverSurface");
        surfaceEffect.Initialize(Context.Services);
        sceneSeedEffect = new ImageEffectShader("RiverSceneSeed", delaySetRenderTargets: true);
        sceneSeedEffect.Initialize(Context);

        contentManager = Context.Services.GetSafeServiceAs<ContentManager>();
        try
        {
            riverResources.Load(Context.GraphicsDevice, contentManager);
        }
        catch (Exception exception)
        {
            Log.Error($"River render resources could not be loaded: {exception.Message}");
            riverResources.Dispose();
        }
        BindStaticRiverResources(Context.GraphicsDevice);
        var meshRenderFeature = RenderSystem?.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
        forwardLightingFeature = meshRenderFeature?.RenderFeatures.OfType<ForwardLightingRenderFeature>().FirstOrDefault();
        bottomShadowMapRenderer = forwardLightingFeature?.ShadowMapRenderer;

        bottomPipelineState = CreatePipelineState(
            Context.GraphicsDevice,
            CreateDualSourceBlendState(),
            CreateDepthStencilState(),
            CreateRasterizerState(wireframe: false, isSurface: false));
        surfacePipelineState = CreatePipelineState(
            Context.GraphicsDevice,
            CreateSurfaceBlendState(),
            CreateDepthStencilState(),
            CreateRasterizerState(wireframe: false, isSurface: true));
    }

    protected override void Destroy()
    {
        renderResources.Dispose();
        if (contentManager != null)
        {
            riverResources.Unload(contentManager);
            contentManager = null;
        }
        else
        {
            riverResources.Dispose();
        }
        bottomEffect?.Dispose();
        bottomEffect = null;
        surfaceEffect?.Dispose();
        surfaceEffect = null;
        sceneSeedEffect?.Dispose();
        sceneSeedEffect = null;
        forwardLightingFeature = null;
        bottomShadowMapRenderer = null;
        bottomPipelineState = null;
        surfacePipelineState = null;

        base.Destroy();
    }

    public override void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);

        if (bottomEffect == null || surfaceEffect == null)
        {
            return;
        }

        var riverParametersSource = FindFirstPreparedRiverObject();
        if (riverParametersSource == null)
        {
            return;
        }

        var riverSettings = GetRiverSettings(riverParametersSource);
        if (riverSettings == null)
        {
            return;
        }

        DebugAssertPreparedRiverParametersMatch(riverParametersSource, riverSettings);
        ApplyBottomParameters(bottomEffect, riverParametersSource, riverSettings);
        ApplySurfaceParameters(surfaceEffect, riverParametersSource, riverSettings);
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        if (bottomEffect == null
            || surfaceEffect == null
            || bottomPipelineState == null
            || surfacePipelineState == null
            || startIndex >= endIndex)
        {
            return;
        }

        var graphicsDevice = context.GraphicsDevice;
        var commandList = context.CommandList;
        Matrix.Invert(ref renderView.View, out var viewInverse);
        var cameraWorldPosition = viewInverse.TranslationVector;
        float riverMaxVisibleCameraHeight = ResolveRiverMaxVisibleCameraHeight(renderViewStage, startIndex, endIndex);
        if (cameraWorldPosition.Y >= riverMaxVisibleCameraHeight)
        {
            return;
        }

        Texture? sceneColor = commandList.RenderTargetCount > 0 ? commandList.RenderTargets[0] : null;
        if (sceneColor == null)
        {
            return;
        }

        renderResources.EnsureResources(graphicsDevice, sceneColor.ViewWidth, sceneColor.ViewHeight);
        if (renderResources.SceneSeedColor == null || renderResources.BottomColor == null || renderResources.BottomDepth == null)
        {
            return;
        }

        bottomEffect.Parameters.Set(RiverBottomKeys._CameraWorldPosition, cameraWorldPosition);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys._CameraWorldPosition, cameraWorldPosition);
        float refractionMaxCameraHeight = ResolveRefractionMaxCameraHeight(renderViewStage, startIndex, endIndex);
        ApplyViewParameters(bottomEffect, renderView);
        ApplyViewParameters(surfaceEffect, renderView);
        ApplySurfaceFrameParameters(surfaceEffect, renderView.ViewSize, renderView.View, (float)context.RenderContext.Time.Total.TotalSeconds);
        BindRefractionMaxCameraHeight(bottomEffect, refractionMaxCameraHeight);
        BindRefractionMaxCameraHeight(surfaceEffect, refractionMaxCameraHeight);

        PrepareRiverSceneLighting(context, renderView);
        ApplyDebugRasterizerState(bottomPipelineState, isSurface: false, nearClipPlane: renderView.NearClipPlane);
        ApplyDebugRasterizerState(surfacePipelineState, isSurface: true, nearClipPlane: renderView.NearClipPlane);

        if (DebugMode != RiverRenderDebugMode.SurfaceOnly)
        {
            using (context.PushRenderTargetsAndRestore())
            {
                SeedSceneColorFromScene(context, renderView, sceneColor, refractionMaxCameraHeight);
                CopySceneSeedToBottomColor(commandList);
                commandList.SetRenderTargetAndViewport(renderResources.BottomDepth, renderResources.BottomColor);
                commandList.Clear(renderResources.BottomDepth, DepthStencilClearOptions.DepthBuffer);
                DrawPass(context, renderView, renderViewStage, startIndex, endIndex, bottomEffect, bottomPipelineState);
            }
        }

        if (DebugMode == RiverRenderDebugMode.BottomOnly)
        {
            return;
        }

        surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionTexture, renderResources.BottomColor);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys._RefractionTextureSize, new Vector2(renderResources.Width, renderResources.Height));
        DrawPass(context, renderView, renderViewStage, startIndex, endIndex, surfaceEffect, surfacePipelineState);
    }

    private RiverRenderObject? FindFirstPreparedRiverObject()
    {
        foreach (var renderObject in RenderObjects)
        {
            if (renderObject is RiverRenderObject riverObject
                && riverObject.Enabled
                && riverObject.MeshDraw != null
                && riverObject.IndexBuffer != null
                && riverObject.VertexBuffer != null)
            {
                return riverObject;
            }
        }

        return null;
    }

    private static RiverRenderSettings? GetRiverSettings(RiverRenderObject riverObject)
    {
        return (riverObject.Source as RiverComponent)?.Settings;
    }

    [Conditional("DEBUG")]
    private void DebugAssertPreparedRiverParametersMatch(RiverRenderObject source, RiverRenderSettings sourceSettings)
    {
        foreach (var renderObject in RenderObjects)
        {
            if (renderObject is not RiverRenderObject riverObject
                || ReferenceEquals(riverObject, source)
                || !riverObject.Enabled
                || riverObject.MeshDraw == null
                || riverObject.IndexBuffer == null
                || riverObject.VertexBuffer == null)
            {
                continue;
            }

            var candidateSettings = GetRiverSettings(riverObject);
            Debug.Assert(
                candidateSettings != null && RiverParametersMatch(source, sourceSettings, riverObject, candidateSettings),
                "RiverRenderFeature binds non-frame river shader parameters once during Prepare; all drawable river objects must share those parameters.");
        }
    }

    private static bool RiverParametersMatch(
        RiverRenderObject source,
        RiverRenderSettings sourceSettings,
        RiverRenderObject candidate,
        RiverRenderSettings candidateSettings)
    {
        return source.MapExtent == candidate.MapExtent
            && source.MapWorldSize == candidate.MapWorldSize
            && sourceSettings.TextureUvScale == candidateSettings.TextureUvScale
            && sourceSettings.FlowNormalUvScale == candidateSettings.FlowNormalUvScale
            && sourceSettings.RiverMaxVisibleCameraHeight == candidateSettings.RiverMaxVisibleCameraHeight
            && sourceSettings.FlowNormalSpeed == candidateSettings.FlowNormalSpeed
            && sourceSettings.RiverFoamFactor == candidateSettings.RiverFoamFactor
            && sourceSettings.NoiseScale == candidateSettings.NoiseScale
            && sourceSettings.NoiseSpeed == candidateSettings.NoiseSpeed
            && sourceSettings.FlattenMultiplier == candidateSettings.FlattenMultiplier
            && sourceSettings.OceanFadeRate == candidateSettings.OceanFadeRate
            && sourceSettings.BankAmount == candidateSettings.BankAmount
            && sourceSettings.BankFade == candidateSettings.BankFade
            && sourceSettings.Depth == candidateSettings.Depth
            && sourceSettings.DepthWidthPower == candidateSettings.DepthWidthPower
            && sourceSettings.DepthFakeFactor == candidateSettings.DepthFakeFactor
            && sourceSettings.ParallaxIterations == candidateSettings.ParallaxIterations
            && sourceSettings.BottomNormalStrength == candidateSettings.BottomNormalStrength
            && sourceSettings.BottomEnvironmentIntensity == candidateSettings.BottomEnvironmentIntensity
            && sourceSettings.FlatMapLerp == candidateSettings.FlatMapLerp
            && sourceSettings.WaterRefractionScale == candidateSettings.WaterRefractionScale
            && sourceSettings.WaterRefractionShoreMaskDepth == candidateSettings.WaterRefractionShoreMaskDepth
            && sourceSettings.WaterRefractionShoreMaskSharpness == candidateSettings.WaterRefractionShoreMaskSharpness
            && sourceSettings.WaterRefractionFade == candidateSettings.WaterRefractionFade
            && sourceSettings.WaterColorShallow == candidateSettings.WaterColorShallow
            && sourceSettings.WaterColorDeep == candidateSettings.WaterColorDeep;
    }

    private float ResolveRefractionMaxCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        float maxHeight = 50.0f;
        for (int index = startIndex; index < endIndex; index++)
        {
            var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
            var renderNode = GetRenderNode(renderNodeReference);
            if (renderNode.RenderObject is RiverRenderObject riverObject && riverObject.Enabled)
            {
                maxHeight = MathF.Max(maxHeight, riverObject.RefractionMaxCameraHeight);
            }
        }

        return maxHeight;
    }

    private float ResolveRiverMaxVisibleCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        float maxVisibleCameraHeight = 3000.0f;
        bool foundRiverObject = false;
        for (int index = startIndex; index < endIndex; index++)
        {
            var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
            var renderNode = GetRenderNode(renderNodeReference);
            if (renderNode.RenderObject is RiverRenderObject riverObject && riverObject.Enabled)
            {
                float riverMaxVisibleCameraHeight = GetRiverSettings(riverObject)?.RiverMaxVisibleCameraHeight ?? 3000.0f;
                maxVisibleCameraHeight = foundRiverObject
                    ? MathF.Max(maxVisibleCameraHeight, riverMaxVisibleCameraHeight)
                    : riverMaxVisibleCameraHeight;
                foundRiverObject = true;
            }
        }

        return maxVisibleCameraHeight;
    }

    private void SeedSceneColorFromScene(RenderDrawContext context, RenderView renderView, Texture? sceneColor, float refractionMaxCameraHeight)
    {
        Debug.Assert(renderResources.SceneSeedColor != null, "River scene seed color target has not been allocated.");
        Debug.Assert(sceneColor != null, "River scene seed requires a scene color render target.");
        Debug.Assert(sceneSeedEffect != null, "River scene seed effect has not been initialized.");
        Debug.Assert(!ReferenceEquals(sceneColor, renderResources.SceneSeedColor), "River scene seed input and output must be different textures.");

        var seedTarget = renderResources.SceneSeedColor!;
        var seedSource = sceneColor!;
        var seedEffect = sceneSeedEffect!;

        var sceneDepthSource = GetPresenterSceneDepthSource(context.GraphicsDevice, seedSource);
        var sceneDepth = context.Resolver.ResolveDepthStencil(sceneDepthSource);
        Debug.Assert(sceneDepth != null, "River scene seed requires a depth buffer that can be resolved as a shader resource.");

        try
        {
            seedEffect.Parameters.Set(DepthBaseKeys.DepthStencil, sceneDepth);
            seedEffect.Parameters.Set(CameraKeys.ViewSize, new Vector2(seedSource.Width, seedSource.Height));
            seedEffect.Parameters.Set(CameraKeys.ZProjection, CameraKeys.ZProjectionACalculate(
                renderView.NearClipPlane,
                renderView.FarClipPlane));
            seedEffect.Parameters.Set(CameraKeys.NearClipPlane, renderView.NearClipPlane);
            seedEffect.Parameters.Set(CameraKeys.FarClipPlane, renderView.FarClipPlane);
            var viewInverse = Matrix.Invert(renderView.View);
            seedEffect.Parameters.Set(TransformationKeys.ViewInverse, ref viewInverse);
            seedEffect.Parameters.Set(TransformationKeys.Eye, new Vector4(viewInverse.TranslationVector, 1.0f));
            seedEffect.Parameters.Set(RiverCommonKeys._RefractionMaxCameraHeight, refractionMaxCameraHeight);
            Matrix.Invert(ref renderView.Projection, out var projectionInverse);
            seedEffect.Parameters.Set(TransformationKeys.ProjectionInverse, ref projectionInverse);
            seedEffect.Parameters.Set(TexturingKeys.Sampler, context.GraphicsDevice.SamplerStates.LinearClamp);
            seedEffect.SetInput(0, seedSource);
            seedEffect.SetOutput(seedTarget);
            seedEffect.Draw(context, "River refraction scene seed");
        }
        finally
        {
            context.Resolver.ReleaseDepthStenctilAsShaderResource(sceneDepth);
        }
    }

    private static Texture GetPresenterSceneDepthSource(GraphicsDevice graphicsDevice, Texture sceneColor)
    {
        var presenter = graphicsDevice.Presenter;
        Debug.Assert(presenter != null, "River scene seed requires GraphicsDevice.Presenter. Offscreen river rendering must provide an explicit scene-depth source before enabling this pass.");

        var sceneDepth = presenter?.DepthStencilBuffer;
        Debug.Assert(sceneDepth != null, "River scene seed requires GraphicsDevice.Presenter.DepthStencilBuffer.");
        Debug.Assert(
            sceneDepth == null || (sceneDepth.ViewWidth == sceneColor.ViewWidth && sceneDepth.ViewHeight == sceneColor.ViewHeight),
            $"River scene seed depth size {sceneDepth?.ViewWidth}x{sceneDepth?.ViewHeight} must match scene color size {sceneColor.ViewWidth}x{sceneColor.ViewHeight}.");

        return sceneDepth!;
    }

    private void CopySceneSeedToBottomColor(CommandList commandList)
    {
        if (renderResources.SceneSeedColor == null || renderResources.BottomColor == null)
        {
            return;
        }

        commandList.CopyRegion(renderResources.SceneSeedColor, 0, null, renderResources.BottomColor, 0);
    }

    private void PrepareRiverSceneLighting(RenderDrawContext context, RenderView renderView)
    {
        if (bottomEffect == null || surfaceEffect == null)
        {
            return;
        }

        var lightingView = renderView.LightingView ?? renderView;
        var renderViewLightData = TryGetRenderViewLightData(lightingView);
        var lights = renderViewLightData?.VisibleLights ?? CollectFallbackVisibleLights(lightingView);
        var (directionalLight, shadowMapTexture) = SelectBottomDirectionalLight(lights, renderViewLightData, lightingView);
        RenderLight? skyboxLight = SelectBottomSkyboxLight(lights);

        BindDirectionalLightFromScene(directionalLight, shadowMapTexture);
        BindEnvironmentFromScene(skyboxLight);
        bottomEffect.UpdateEffect(context.GraphicsDevice);
        surfaceEffect.UpdateEffect(context.GraphicsDevice);
    }

    private ForwardLightingRenderFeature.RenderViewLightData? TryGetRenderViewLightData(RenderView lightingView)
    {
        if (forwardLightingFeature != null
            && RenderViewDatasField?.GetValue(forwardLightingFeature) is Dictionary<RenderView, ForwardLightingRenderFeature.RenderViewLightData> renderViewDatas
            && renderViewDatas.TryGetValue(lightingView, out var renderViewLightData))
        {
            return renderViewLightData;
        }

        return null;
    }

    private IReadOnlyList<RenderLight>? CollectFallbackVisibleLights(RenderView lightingView)
    {
        fallbackVisibleLights.Clear();

        var lights = Context.VisibilityGroup.Tags.Get(ForwardLightingRenderFeature.CurrentLights);
        if (lights == null)
        {
            return null;
        }

        var frustum = lightingView.Frustum;
        foreach (var light in lights)
        {
            if (light.Type is IDirectLight directLight
                && directLight.HasBoundingBox
                && !frustum.Contains(ref light.BoundingBoxExt))
            {
                continue;
            }

            fallbackVisibleLights.Add(light);
        }

        return fallbackVisibleLights;
    }

    private (RenderLight? DirectionalLight, LightShadowMapTexture? ShadowMapTexture) SelectBottomDirectionalLight(
        IReadOnlyList<RenderLight>? lights,
        ForwardLightingRenderFeature.RenderViewLightData? renderViewLightData,
        RenderView lightingView)
    {
        RenderLight? bestDirectionalLight = null;
        LightShadowMapTexture? bestShadowMapTexture = null;

        if (renderViewLightData != null)
        {
            foreach (var light in renderViewLightData.VisibleLightsWithShadows)
            {
                if (light.Type is not LightDirectional)
                {
                    continue;
                }

                var shadowMapTexture = TryGetSceneShadowMapTexture(renderViewLightData, lightingView, light);
                if (shadowMapTexture == null)
                {
                    continue;
                }

                if (bestDirectionalLight == null || light.Intensity > bestDirectionalLight.Intensity)
                {
                    bestDirectionalLight = light;
                    bestShadowMapTexture = shadowMapTexture;
                }
            }
        }

        if (bestDirectionalLight != null)
        {
            return (bestDirectionalLight, bestShadowMapTexture);
        }

        if (lights != null)
        {
            foreach (var light in lights)
            {
                if (light.Type is not LightDirectional)
                {
                    continue;
                }

                if (bestDirectionalLight == null || light.Intensity > bestDirectionalLight.Intensity)
                {
                    bestDirectionalLight = light;
                }
            }
        }

        return (bestDirectionalLight, bestDirectionalLight != null ? TryGetSceneShadowMapTexture(renderViewLightData, lightingView, bestDirectionalLight) : null);
    }

    private static RenderLight? SelectBottomSkyboxLight(IReadOnlyList<RenderLight>? lights)
    {
        if (lights == null)
        {
            return null;
        }

        RenderLight? bestSkyboxLight = null;
        RenderLight? bestSkyboxWithCubemap = null;
        foreach (var light in lights)
        {
            if (light.Type is not LightSkybox)
            {
                continue;
            }

            if (bestSkyboxLight == null || light.Intensity > bestSkyboxLight.Intensity)
            {
                bestSkyboxLight = light;
            }

            if (TryGetSceneEnvironmentTexture(light) != null
                && (bestSkyboxWithCubemap == null || light.Intensity > bestSkyboxWithCubemap.Intensity))
            {
                bestSkyboxWithCubemap = light;
            }
        }

        return bestSkyboxWithCubemap ?? bestSkyboxLight;
    }

    private LightShadowMapTexture? TryGetSceneShadowMapTexture(
        ForwardLightingRenderFeature.RenderViewLightData? renderViewLightData,
        RenderView lightingView,
        RenderLight directionalLight)
    {
        if (renderViewLightData?.RenderLightsWithShadows.TryGetValue(directionalLight, out var shadowMapTexture) == true)
        {
            return shadowMapTexture;
        }

        return bottomShadowMapRenderer?.FindShadowMap(lightingView, directionalLight);
    }

    private void BindDirectionalLightFromScene(RenderLight? directionalLight, LightShadowMapTexture? shadowMapTexture)
    {
        if (bottomEffect == null || surfaceEffect == null)
        {
            return;
        }

        Vector3 sceneSunDirection = directionalLight?.Direction ?? new Vector3(0.0f, -1.0f, 0.0f);
        Vector3 sceneSunColor = directionalLight?.Color.ToVector3() ?? Vector3.Zero;
        int cascadeCount = 0;
        float shadowBlendCascades = 0.0f;
        float sceneShadowDepthBias = 0.01f;
        float[] shadowCascadeSplits = [0.0f, 0.0f, 0.0f, 0.0f];
        Matrix[] worldToShadowCascadeUv =
        [
            Matrix.Identity,
            Matrix.Identity,
            Matrix.Identity,
            Matrix.Identity,
        ];
        Texture? sceneShadowMapTexture = null;

        if (shadowMapTexture?.ShaderData is LightDirectionalShadowMapRenderer.ShaderData shaderData)
        {
            cascadeCount = Math.Min(Math.Min(shadowMapTexture.CascadeCount, shaderData.CascadeSplits.Length), worldToShadowCascadeUv.Length);
            Array.Copy(shaderData.CascadeSplits, shadowCascadeSplits, cascadeCount);
            Array.Copy(shaderData.WorldToShadowCascadeUV, worldToShadowCascadeUv, cascadeCount);
            shadowBlendCascades = (shadowMapTexture.ShadowType & LightShadowType.BlendCascade) != 0 ? 1.0f : 0.0f;
            sceneShadowDepthBias = shaderData.DepthBias;
            sceneShadowMapTexture = shaderData.Texture;
        }

        BindDirectionalLightToEffect(bottomEffect?.Parameters, sceneSunDirection, sceneSunColor, cascadeCount, shadowBlendCascades, sceneShadowDepthBias, shadowCascadeSplits, worldToShadowCascadeUv, sceneShadowMapTexture);
        BindDirectionalLightToEffect(surfaceEffect?.Parameters, sceneSunDirection, sceneSunColor, cascadeCount, shadowBlendCascades, sceneShadowDepthBias, shadowCascadeSplits, worldToShadowCascadeUv, sceneShadowMapTexture);
    }

    private static void BindDirectionalLightToEffect(
        ParameterCollection? parameters,
        Vector3 sceneSunDirection,
        Vector3 sceneSunColor,
        int cascadeCount,
        float shadowBlendCascades,
        float sceneShadowDepthBias,
        float[] shadowCascadeSplits,
        Matrix[] worldToShadowCascadeUv,
        Texture? sceneShadowMapTexture)
    {
        if (parameters == null)
        {
            return;
        }

        parameters.Set(RiverStrideLightingKeys._SceneSunDirection, sceneSunDirection);
        parameters.Set(RiverStrideLightingKeys._SceneSunColor, sceneSunColor);
        parameters.Set(RiverStrideLightingKeys._SceneShadowCascadeCount, cascadeCount);
        parameters.Set(RiverStrideLightingKeys._SceneShadowBlendCascades, shadowBlendCascades);
        parameters.Set(RiverStrideLightingKeys._SceneShadowDepthBias, sceneShadowDepthBias);
        parameters.Set(RiverStrideLightingKeys._SceneShadowCascadeSplits, shadowCascadeSplits);
        parameters.Set(RiverStrideLightingKeys._SceneWorldToShadowCascadeUV, worldToShadowCascadeUv);
        parameters.SetObject(RiverStrideLightingKeys.SceneShadowMapTexture, sceneShadowMapTexture);
    }

    private void BindEnvironmentFromScene(RenderLight? skyboxLight)
    {
        if (bottomEffect == null || surfaceEffect == null)
        {
            return;
        }

        Texture? bottomEnvironment = TryGetSceneEnvironmentTexture(skyboxLight);
        Debug.Assert(bottomEnvironment != null, "River bottom requires a real scene skybox cubemap.");
        if (bottomEnvironment == null)
        {
            throw new InvalidOperationException("River bottom requires a real scene skybox cubemap.");
        }

        Matrix skyMatrix = Matrix.Identity;
        float intensity = 1.0f;
        if (skyboxLight != null)
        {
            var rotation = Quaternion.RotationMatrix(skyboxLight.WorldMatrix);
            skyMatrix = Matrix.Invert(Matrix.RotationQuaternion(rotation));
            intensity = skyboxLight.Intensity;
        }

        BindEnvironmentToEffect(bottomEffect?.Parameters, bottomEnvironment, skyMatrix, intensity);
        BindEnvironmentToEffect(surfaceEffect?.Parameters, bottomEnvironment, skyMatrix, intensity);
    }

    private static void BindEnvironmentToEffect(ParameterCollection? parameters, Texture? environmentTexture, Matrix skyMatrix, float intensity)
    {
        if (parameters == null)
        {
            return;
        }

        SetTexture(parameters, RiverStrideLightingKeys.EnvironmentMapTexture, environmentTexture);
        parameters.Set(RiverStrideLightingKeys._EnvironmentSkyMatrix, skyMatrix);
        parameters.Set(RiverStrideLightingKeys._EnvironmentIntensity, intensity);
        parameters.Set(RiverStrideLightingKeys._EnvironmentMipCount, (float)Math.Max(environmentTexture?.MipLevelCount ?? 1, 1));
    }

    private static Texture? TryGetSceneEnvironmentTexture(RenderLight? skyboxLight)
    {
        if (skyboxLight?.Type is not LightSkybox lightSkybox)
        {
            return null;
        }

        return lightSkybox.Skybox?.SpecularLightingParameters.Get(SkyboxKeys.CubeMap);
    }

    private static void ApplyBottomParameters(DynamicEffectInstance effect, RiverRenderObject riverObject, RiverRenderSettings settings)
    {
        effect.Parameters.Set(RiverBottomKeys._MapExtent, riverObject.MapExtent);
        effect.Parameters.Set(RiverBottomKeys._TextureUvScale, settings.TextureUvScale);
        effect.Parameters.Set(RiverBottomKeys._OceanFadeRate, settings.OceanFadeRate);
        effect.Parameters.Set(RiverBottomKeys._BankAmount, settings.BankAmount);
        effect.Parameters.Set(RiverBottomKeys._BankFade, settings.BankFade);
        effect.Parameters.Set(RiverBottomKeys._Depth, settings.Depth);
        effect.Parameters.Set(RiverBottomKeys._DepthWidthPower, settings.DepthWidthPower);
        effect.Parameters.Set(RiverBottomKeys._DepthFakeFactor, settings.DepthFakeFactor);
        effect.Parameters.Set(RiverBottomKeys._ParallaxIterations, settings.ParallaxIterations);
        effect.Parameters.Set(RiverBottomKeys._BottomNormalStrength, settings.BottomNormalStrength);
        effect.Parameters.Set(RiverBottomKeys._BottomEnvironmentIntensity, settings.BottomEnvironmentIntensity);
    }

    private static void ApplySurfaceParameters(DynamicEffectInstance effect, RiverRenderObject riverObject, RiverRenderSettings settings)
    {
        effect.Parameters.Set(RiverSurfaceKeys._MapExtent, riverObject.MapExtent);
        effect.Parameters.Set(RiverSurfaceKeys._MapWorldSize, riverObject.MapWorldSize);
        effect.Parameters.Set(RiverSurfaceKeys._FlowNormalUvScale, settings.FlowNormalUvScale);
        effect.Parameters.Set(RiverSurfaceKeys._FlowNormalSpeed, settings.FlowNormalSpeed);
        effect.Parameters.Set(RiverSurfaceKeys._RiverFoamFactor, settings.RiverFoamFactor);
        effect.Parameters.Set(RiverSurfaceKeys._NoiseScale, settings.NoiseScale);
        effect.Parameters.Set(RiverSurfaceKeys._NoiseSpeed, settings.NoiseSpeed);
        effect.Parameters.Set(RiverSurfaceKeys._FlattenMult, settings.FlattenMultiplier);
        effect.Parameters.Set(RiverSurfaceKeys._Depth, settings.Depth);
        effect.Parameters.Set(RiverSurfaceKeys._DepthWidthPower, settings.DepthWidthPower);
        effect.Parameters.Set(RiverSurfaceKeys._BankFade, settings.BankFade);
        effect.Parameters.Set(RiverSurfaceKeys._FlatMapLerp, settings.FlatMapLerp);
        effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionScale, settings.WaterRefractionScale);
        effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionShoreMaskDepth, settings.WaterRefractionShoreMaskDepth);
        effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionShoreMaskSharpness, settings.WaterRefractionShoreMaskSharpness);
        effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionFade, settings.WaterRefractionFade);
        effect.Parameters.Set(RiverSurfaceKeys.WaterColorShallow, settings.WaterColorShallow);
        effect.Parameters.Set(RiverSurfaceKeys.WaterColorDeep, settings.WaterColorDeep);
    }

    private static void ApplyViewParameters(DynamicEffectInstance effect, RenderView renderView)
    {
        effect.Parameters.Set(TransformationKeys.ViewProjection, renderView.ViewProjection);
        effect.Parameters.Set(CameraKeys.ViewSize, renderView.ViewSize);
    }

    private static void ApplySurfaceFrameParameters(DynamicEffectInstance effect, Vector2 viewSize, Matrix viewMatrix, float globalTime)
    {
        effect.Parameters.Set(RiverSurfaceKeys._ViewSize, viewSize);
        effect.Parameters.Set(RiverSurfaceKeys._ViewMatrix, viewMatrix);
        effect.Parameters.Set(RiverSurfaceKeys._GlobalTime, globalTime);
    }

    private static void BindRefractionMaxCameraHeight(DynamicEffectInstance effect, float refractionMaxCameraHeight)
    {
        effect.Parameters.Set(RiverCommonKeys._RefractionMaxCameraHeight, refractionMaxCameraHeight);
    }

    private void DrawPass(
        RenderDrawContext context,
        RenderView renderView,
        RenderViewStage renderViewStage,
        int startIndex,
        int endIndex,
        DynamicEffectInstance effect,
        MutablePipelineState pipelineState)
    {
        var graphicsContext = context.GraphicsContext;
        var commandList = context.CommandList;

        pipelineState.State.RootSignature = effect.RootSignature;
        pipelineState.State.EffectBytecode = effect.Effect.Bytecode;
        pipelineState.State.InputElements = RiverInputElements;
        pipelineState.State.Output.CaptureState(commandList);
        pipelineState.Update();
        if (pipelineState.CurrentState == null)
        {
            return;
        }

        commandList.SetPipelineState(pipelineState.CurrentState);

        for (int index = startIndex; index < endIndex; index++)
        {
            var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
            var renderNode = GetRenderNode(renderNodeReference);
            if (renderNode.RenderObject is not RiverRenderObject riverObject
                || !riverObject.Enabled
                || riverObject.MeshDraw == null
                || riverObject.IndexBuffer == null
                || riverObject.VertexBuffer == null)
            {
                continue;
            }

            effect.Parameters.Set(TransformationKeys.World, riverObject.World);
            effect.Parameters.Set(TransformationKeys.WorldView, riverObject.World * renderView.View);
            effect.Parameters.Set(TransformationKeys.WorldViewProjection, riverObject.World * renderView.ViewProjection);

            effect.Apply(graphicsContext);
            commandList.SetVertexBuffer(0, riverObject.VertexBuffer, 0, RiverVertex.Layout.VertexStride);
            commandList.SetIndexBuffer(riverObject.IndexBuffer, 0, true);
            commandList.DrawIndexed(riverObject.IndexCount);
        }
    }

    private void ApplyDebugRasterizerState(MutablePipelineState pipelineState, bool isSurface, float nearClipPlane)
    {
        pipelineState.State.RasterizerState = CreateRasterizerStateForRenderView(DebugMode == RiverRenderDebugMode.Wireframe, isSurface, nearClipPlane);
    }

    private void BindStaticRiverResources(GraphicsDevice graphicsDevice)
    {
        if (bottomEffect == null || surfaceEffect == null)
        {
            return;
        }

        bottomEffect.Parameters.Set(RiverBottomKeys._WaterHeight, 3.0f);
        bottomEffect.Parameters.Set(RiverBottomKeys._WorldToMapUnitScale, 0.5f);
        bottomEffect.Parameters.Set(RiverBottomKeys.BottomTextureSampler, graphicsDevice.SamplerStates.LinearWrap);
        SetTexture(bottomEffect.Parameters, RiverBottomKeys.BottomDiffuseTexture, riverResources.BottomDiffuse);
        SetTexture(bottomEffect.Parameters, RiverBottomKeys.BottomNormalTexture, riverResources.BottomNormal);
        SetTexture(bottomEffect.Parameters, RiverBottomKeys.BottomPropertiesTexture, riverResources.BottomProperties);
        SetTexture(bottomEffect.Parameters, RiverBottomKeys.BottomDepthTexture, riverResources.BottomDepth);
        bottomEffect.Parameters.Set(RiverStrideLightingKeys.EnvironmentMapSampler, graphicsDevice.SamplerStates.LinearClamp);

        surfaceEffect.Parameters.Set(RiverSurfaceKeys.WaterTextureSampler, graphicsDevice.SamplerStates.LinearWrap);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys.WaterColorSampler, graphicsDevice.SamplerStates.LinearWrap);
        surfaceEffect.Parameters.Set(RiverStrideLightingKeys.EnvironmentMapSampler, graphicsDevice.SamplerStates.LinearClamp);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.AmbientNormalTexture, riverResources.AmbientNormal);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FlowNormalTexture, riverResources.FlowNormal);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FoamTexture, riverResources.Foam);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FoamRampTexture, riverResources.FoamRamp);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FoamMapTexture, riverResources.FoamMap);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FoamNoiseTexture, riverResources.FoamNoise);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.WaterColorTexture, riverResources.WaterColor);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.ReflectionSpecularTexture, riverResources.ReflectionSpecular);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys.ReflectionSpecularSampler, graphicsDevice.SamplerStates.LinearClamp);
    }

    private static void SetTexture(ParameterCollection parameters, ObjectParameterKey<Texture> key, Texture? texture)
    {
        parameters.SetObject(key, texture);
    }

    private static BlendStateDescription CreateDualSourceBlendState()
    {
        var blendState = BlendStateDescription.Default;
        blendState.RenderTargets[0].BlendEnable = true;
        blendState.RenderTargets[0].ColorSourceBlend = Blend.SecondarySourceAlpha;
        blendState.RenderTargets[0].ColorDestinationBlend = Blend.InverseSecondarySourceAlpha;
        blendState.RenderTargets[0].AlphaSourceBlend = Blend.SecondarySourceAlpha;
        blendState.RenderTargets[0].AlphaDestinationBlend = Blend.InverseSecondarySourceAlpha;
        return blendState;
    }

    private static BlendStateDescription CreateSurfaceBlendState()
    {
        var blendState = BlendStateDescription.Default;
        blendState.RenderTargets[0].BlendEnable = true;
        blendState.RenderTargets[0].ColorSourceBlend = Blend.SourceAlpha;
        blendState.RenderTargets[0].ColorDestinationBlend = Blend.InverseSourceAlpha;
        blendState.RenderTargets[0].AlphaSourceBlend = Blend.One;
        blendState.RenderTargets[0].AlphaDestinationBlend = Blend.Zero;
        blendState.RenderTargets[0].ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue;
        return blendState;
    }

    private static MutablePipelineState CreatePipelineState(
        GraphicsDevice graphicsDevice,
        BlendStateDescription blendState,
        DepthStencilStateDescription depthStencilState,
        RasterizerStateDescription rasterizerState)
    {
        var pipelineState = new MutablePipelineState(graphicsDevice);
        pipelineState.State.PrimitiveType = PrimitiveType.TriangleList;
        pipelineState.State.InputElements = RiverInputElements;
        pipelineState.State.BlendState = blendState;
        pipelineState.State.DepthStencilState = depthStencilState;
        pipelineState.State.RasterizerState = rasterizerState;
        return pipelineState;
    }

    private static RasterizerStateDescription CreateRasterizerState(bool wireframe, bool isSurface)
    {
        return new RasterizerStateDescription(CullMode.Back)
        {
            FillMode = wireframe ? FillMode.Wireframe : FillMode.Solid,
            DepthBias = isSurface ? SurfaceDepthBias : BottomDepthBias,
            SlopeScaleDepthBias = isSurface ? SurfaceSlopeScaleDepthBias : BottomSlopeScaleDepthBias,
        };
    }

    private static RasterizerStateDescription CreateRasterizerStateForRenderView(bool wireframe, bool isSurface, float nearClipPlane)
    {
        return new RasterizerStateDescription(CullMode.Back)
        {
            FillMode = wireframe ? FillMode.Wireframe : FillMode.Solid,
            DepthBias = isSurface ? ResolveSurfaceDepthBias(nearClipPlane) : BottomDepthBias,
            SlopeScaleDepthBias = isSurface ? SurfaceSlopeScaleDepthBias : BottomSlopeScaleDepthBias,
        };
    }

    private static int ResolveSurfaceDepthBias(float nearClipPlane)
    {
        float normalizedNear = MathUtil.Clamp(nearClipPlane / TargetSurfaceDepthBiasNearClip, 0.0f, 1.0f);
        float scaledMagnitude = Math.Abs(SurfaceDepthBias) * MathF.Pow(normalizedNear, SurfaceDepthBiasNearClipScaleExponent);
        int depthBias = Math.Max(Math.Abs(BottomDepthBias), (int)MathF.Round(scaledMagnitude));
        return -depthBias;
    }

    private static DepthStencilStateDescription CreateDepthStencilState()
    {
        return new DepthStencilStateDescription(depthEnable: true, depthWriteEnable: false)
        {
            DepthBufferFunction = CompareFunction.Less
        };
    }
}
