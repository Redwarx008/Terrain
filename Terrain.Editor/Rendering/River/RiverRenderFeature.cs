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
using Terrain.Editor.Rendering;
using Terrain.Editor.Services;

namespace Terrain.Editor.Rendering.River;

public enum RiverRenderDebugMode
{
    Normal,
    Wireframe,
    BottomOnly,
    SurfaceOnly,
}

public sealed class RiverRenderFeature : RootRenderFeature
{
    private static readonly InputElementDescription[] RiverInputElements = RiverVertex.Layout.CreateInputElements();
    private static readonly FieldInfo? RenderViewDatasField = typeof(ForwardLightingRenderFeature).GetField("renderViewDatas", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");

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
    private bool loggedMissingSurfaceTerrain;

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
        riverResources.Load(Context.GraphicsDevice, contentManager);
        var meshRenderFeature = RenderSystem?.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
        forwardLightingFeature = meshRenderFeature?.RenderFeatures.OfType<ForwardLightingRenderFeature>().FirstOrDefault();
        bottomShadowMapRenderer = forwardLightingFeature?.ShadowMapRenderer;

        bottomPipelineState = CreatePipelineState(Context.GraphicsDevice, CreateDualSourceBlendState(), DepthStencilStates.DepthRead);
        surfacePipelineState = CreatePipelineState(Context.GraphicsDevice, CreateSurfaceBlendState(), DepthStencilStates.DepthRead);
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
        int viewWidth = Math.Max(1, (int)renderView.ViewSize.X);
        int viewHeight = Math.Max(1, (int)renderView.ViewSize.Y);
        renderResources.EnsureResources(graphicsDevice, viewWidth, viewHeight);
        if (renderResources.SceneSeedColor == null || renderResources.BottomColor == null || renderResources.BottomDepth == null)
        {
            return;
        }
        Texture? sceneColor = commandList.RenderTargetCount > 0 ? commandList.RenderTargets[0] : null;

        Matrix.Invert(ref renderView.View, out var viewInverse);
        var cameraWorldPosition = viewInverse.TranslationVector;
        bottomEffect.Parameters.Set(RiverBottomKeys._CameraWorldPosition, cameraWorldPosition);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys._CameraWorldPosition, cameraWorldPosition);

        PrepareBottomSceneLighting(context, renderView);
        surfaceEffect.UpdateEffect(graphicsDevice);
        BindRiverTextures(graphicsDevice);
        ApplyDebugRasterizerState(bottomPipelineState);
        ApplyDebugRasterizerState(surfacePipelineState);

        if (DebugMode != RiverRenderDebugMode.SurfaceOnly)
        {
            using (context.PushRenderTargetsAndRestore())
            {
                SeedSceneColorFromScene(context, renderView, sceneColor);
                CopySceneSeedToBottomColor(commandList);
                commandList.SetRenderTargetAndViewport(renderResources.BottomDepth, renderResources.BottomColor);
                commandList.Clear(renderResources.BottomDepth, DepthStencilClearOptions.DepthBuffer);
                DrawPass(context, renderView, renderViewStage, startIndex, endIndex, bottomEffect, bottomPipelineState, null);
            }
        }

        if (DebugMode == RiverRenderDebugMode.BottomOnly)
        {
            return;
        }

        surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionTexture, renderResources.BottomColor);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);
        if (!BindSurfaceRequiredInputs())
        {
            return;
        }
        DrawPass(context, renderView, renderViewStage, startIndex, endIndex, surfaceEffect, surfacePipelineState, renderResources.BottomColor);
    }

    private void SeedSceneColorFromScene(RenderDrawContext context, RenderView renderView, Texture? sceneColor)
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

    private void PrepareBottomSceneLighting(RenderDrawContext context, RenderView renderView)
    {
        if (bottomEffect == null)
        {
            return;
        }

        var lightingView = renderView.LightingView ?? renderView;
        var renderViewLightData = TryGetRenderViewLightData(lightingView);
        var lights = renderViewLightData?.VisibleLights ?? CollectFallbackVisibleLights(lightingView);
        var (directionalLight, shadowMapTexture) = SelectBottomDirectionalLight(lights, renderViewLightData, lightingView);
        RenderLight? skyboxLight = SelectBottomSkyboxLight(lights);

        BindBottomDirectionalLightFromScene(directionalLight, shadowMapTexture);
        BindBottomEnvironmentFromScene(skyboxLight);
        bottomEffect.UpdateEffect(context.GraphicsDevice);
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

    private void BindBottomDirectionalLightFromScene(RenderLight? directionalLight, LightShadowMapTexture? shadowMapTexture)
    {
        if (bottomEffect == null)
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

        bottomEffect.Parameters.Set(RiverBottomKeys._SceneSunDirection, sceneSunDirection);
        bottomEffect.Parameters.Set(RiverBottomKeys._SceneSunColor, sceneSunColor);
        bottomEffect.Parameters.Set(RiverBottomKeys._SceneShadowCascadeCount, cascadeCount);
        bottomEffect.Parameters.Set(RiverBottomKeys._SceneShadowBlendCascades, shadowBlendCascades);
        bottomEffect.Parameters.Set(RiverBottomKeys._SceneShadowDepthBias, sceneShadowDepthBias);
        bottomEffect.Parameters.Set(RiverBottomKeys._SceneShadowCascadeSplits, shadowCascadeSplits);
        bottomEffect.Parameters.Set(RiverBottomKeys._SceneWorldToShadowCascadeUV, worldToShadowCascadeUv);
        bottomEffect.Parameters.SetObject(RiverBottomKeys.SceneShadowMapTexture, sceneShadowMapTexture);
    }

    private void BindBottomEnvironmentFromScene(RenderLight? skyboxLight)
    {
        if (bottomEffect == null)
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

        SetTexture(bottomEffect.Parameters, RiverBottomKeys.EnvironmentMapTexture, bottomEnvironment);
        bottomEffect.Parameters.Set(RiverBottomKeys._EnvironmentSkyMatrix, skyMatrix);
        bottomEffect.Parameters.Set(RiverBottomKeys._EnvironmentIntensity, intensity);
        bottomEffect.Parameters.Set(RiverBottomKeys._EnvironmentMipCount, (float)Math.Max(bottomEnvironment?.MipLevelCount ?? 1, 1));
    }

    private static Texture? TryGetSceneEnvironmentTexture(RenderLight? skyboxLight)
    {
        if (skyboxLight?.Type is not LightSkybox lightSkybox)
        {
            return null;
        }

        return lightSkybox.Skybox?.SpecularLightingParameters.Get(SkyboxKeys.CubeMap);
    }

    private static void ApplyBottomParameters(DynamicEffectInstance effect, RiverRenderObject riverObject)
    {
        effect.Parameters.Set(RiverBottomKeys._MapExtent, riverObject.MapExtent);
        effect.Parameters.Set(RiverBottomKeys._TextureUvScale, riverObject.TextureUvScale);
        effect.Parameters.Set(RiverBottomKeys._OceanFadeRate, riverObject.OceanFadeRate);
        effect.Parameters.Set(RiverBottomKeys._WaterHeight, 3.0f);
        effect.Parameters.Set(RiverBottomKeys._BankAmount, riverObject.BankAmount);
        effect.Parameters.Set(RiverBottomKeys._BankFade, riverObject.BankFade);
        effect.Parameters.Set(RiverBottomKeys._Depth, riverObject.Depth);
        effect.Parameters.Set(RiverBottomKeys._DepthWidthPower, riverObject.DepthWidthPower);
        effect.Parameters.Set(RiverBottomKeys._WorldToMapUnitScale, 0.5f);
        effect.Parameters.Set(RiverBottomKeys._DepthFakeFactor, riverObject.DepthFakeFactor);
        effect.Parameters.Set(RiverBottomKeys._ParallaxIterations, riverObject.ParallaxIterations);
        effect.Parameters.Set(RiverBottomKeys._BottomNormalStrength, riverObject.BottomNormalStrength);
        effect.Parameters.Set(RiverBottomKeys._BottomEnvironmentIntensity, riverObject.BottomEnvironmentIntensity);
    }

    private static void ApplySurfaceParameters(DynamicEffectInstance effect, RiverRenderObject riverObject, Vector2 viewSize, Matrix viewMatrix, float globalTime)
    {
        effect.Parameters.Set(RiverSurfaceKeys._ViewSize, viewSize);
        effect.Parameters.Set(RiverSurfaceKeys._ViewMatrix, viewMatrix);
        effect.Parameters.Set(RiverSurfaceKeys._MapExtent, riverObject.MapExtent);
        effect.Parameters.Set(RiverSurfaceKeys._MapWorldSize, riverObject.MapWorldSize);
        effect.Parameters.Set(RiverSurfaceKeys._FlowNormalUvScale, riverObject.FlowNormalUvScale);
        effect.Parameters.Set(RiverSurfaceKeys._FlowNormalSpeed, riverObject.FlowNormalSpeed);
        effect.Parameters.Set(RiverSurfaceKeys._RiverFoamFactor, riverObject.RiverFoamFactor);
        effect.Parameters.Set(RiverSurfaceKeys._NoiseScale, riverObject.NoiseScale);
        effect.Parameters.Set(RiverSurfaceKeys._NoiseSpeed, riverObject.NoiseSpeed);
        effect.Parameters.Set(RiverSurfaceKeys._FlattenMult, riverObject.FlattenMultiplier);
        effect.Parameters.Set(RiverSurfaceKeys._BankFade, riverObject.BankFade);
        effect.Parameters.Set(RiverSurfaceKeys._Depth, riverObject.Depth);
        effect.Parameters.Set(RiverSurfaceKeys._DepthWidthPower, riverObject.DepthWidthPower);
        effect.Parameters.Set(RiverSurfaceKeys._GlobalTime, globalTime);
        effect.Parameters.Set(RiverSurfaceKeys._FlatMapLerp, riverObject.FlatMapLerp);
        effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionScale, riverObject.WaterRefractionScale);
        effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionShoreMaskDepth, riverObject.WaterRefractionShoreMaskDepth);
        effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionShoreMaskSharpness, riverObject.WaterRefractionShoreMaskSharpness);
        effect.Parameters.Set(RiverSurfaceKeys._WaterRefractionFade, riverObject.WaterRefractionFade);
        effect.Parameters.Set(RiverSurfaceKeys.WaterColorShallow, riverObject.WaterColorShallow);
        effect.Parameters.Set(RiverSurfaceKeys.WaterColorDeep, riverObject.WaterColorDeep);
    }

    private void DrawPass(
        RenderDrawContext context,
        RenderView renderView,
        RenderViewStage renderViewStage,
        int startIndex,
        int endIndex,
        DynamicEffectInstance effect,
        MutablePipelineState pipelineState,
        Texture? refractionTexture)
    {
        var graphicsContext = context.GraphicsContext;
        var commandList = context.CommandList;
        var graphicsDevice = context.GraphicsDevice;

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
            effect.Parameters.Set(TransformationKeys.ViewProjection, renderView.ViewProjection);
            effect.Parameters.Set(CameraKeys.ViewSize, renderView.ViewSize);
            if (ReferenceEquals(effect, surfaceEffect))
            {
                ApplySurfaceParameters(effect, riverObject, renderView.ViewSize, renderView.View, (float)context.RenderContext.Time.Total.TotalSeconds);
            }
            else if (ReferenceEquals(effect, bottomEffect))
            {
                ApplyBottomParameters(effect, riverObject);
            }
            if (refractionTexture != null)
            {
                effect.Parameters.Set(RiverSurfaceKeys.RefractionTexture, refractionTexture);
                effect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);
            }

            effect.Apply(graphicsContext);
            commandList.SetVertexBuffer(0, riverObject.VertexBuffer, 0, RiverVertex.Layout.VertexStride);
            commandList.SetIndexBuffer(riverObject.IndexBuffer, 0, true);
            commandList.DrawIndexed(riverObject.IndexCount);
        }
    }

    private void ApplyDebugRasterizerState(MutablePipelineState pipelineState)
    {
        pipelineState.State.RasterizerState = CreateRasterizerState(DebugMode == RiverRenderDebugMode.Wireframe);
    }

    private void BindRiverTextures(GraphicsDevice graphicsDevice)
    {
        if (bottomEffect == null || surfaceEffect == null)
        {
            return;
        }

        bottomEffect.Parameters.Set(RiverBottomKeys.BottomTextureSampler, graphicsDevice.SamplerStates.LinearWrap);
        SetTexture(bottomEffect.Parameters, RiverBottomKeys.BottomDiffuseTexture, riverResources.BottomDiffuse);
        SetTexture(bottomEffect.Parameters, RiverBottomKeys.BottomNormalTexture, riverResources.BottomNormal);
        SetTexture(bottomEffect.Parameters, RiverBottomKeys.BottomPropertiesTexture, riverResources.BottomProperties);
        SetTexture(bottomEffect.Parameters, RiverBottomKeys.BottomDepthTexture, riverResources.BottomDepth);
        bottomEffect.Parameters.Set(RiverBottomKeys.EnvironmentMapSampler, graphicsDevice.SamplerStates.LinearClamp);

        surfaceEffect.Parameters.Set(RiverSurfaceKeys.WaterTextureSampler, graphicsDevice.SamplerStates.LinearWrap);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys.WaterColorSampler, graphicsDevice.SamplerStates.LinearWrap);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.AmbientNormalTexture, riverResources.AmbientNormal);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FlowNormalTexture, riverResources.FlowNormal);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FoamTexture, riverResources.Foam);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FoamRampTexture, riverResources.FoamRamp);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FoamMapTexture, riverResources.FoamMap);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.FoamNoiseTexture, riverResources.FoamNoise);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.ShadowNoiseTexture, riverResources.ShadowColor);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.WaterColorTexture, riverResources.WaterColor);
        SetTexture(surfaceEffect.Parameters, RiverSurfaceKeys.ReflectionSpecularTexture, riverResources.ReflectionSpecular);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys.ReflectionSpecularSampler, graphicsDevice.SamplerStates.LinearClamp);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys.ShadowNoiseSampler, graphicsDevice.SamplerStates.LinearWrap);
        surfaceEffect.Parameters.Set(RiverSurfaceKeys.TerrainHeightSampler, graphicsDevice.SamplerStates.LinearClamp);
    }

    private bool BindSurfaceRequiredInputs()
    {
        Debug.Assert(surfaceEffect != null);

        bool terrainBound = TryBindEditorTerrainInputs();
        if (!terrainBound)
        {
            if (!loggedMissingSurfaceTerrain)
            {
                Log.Warning("River surface map-lighting skipped because editor terrain height slices are not available.");
                loggedMissingSurfaceTerrain = true;
            }

            return false;
        }

        return true;
    }

    private bool TryBindEditorTerrainInputs()
    {
        if (surfaceEffect == null)
        {
            return false;
        }

        var terrain = Context.VisibilityGroup.RenderObjects
            .OfType<EditorTerrainRenderObject>()
            .FirstOrDefault(static renderObject => renderObject.TerrainEntity != null);
        var entity = terrain?.TerrainEntity;
        if (terrain == null || entity == null || entity.Slices.Count == 0)
        {
            return false;
        }

        var parameters = surfaceEffect.Parameters;
        SetEditorTerrainSliceTextures(parameters, entity, terrain);
        SetEditorTerrainSliceBounds(parameters, entity);
        parameters.Set(RiverSurfaceKeys.SliceCount, entity.Slices.Count);
        parameters.Set(RiverSurfaceKeys.HeightScale, entity.HeightScale);

        parameters.Set(RiverSurfaceKeys._TerrainWorldOffsetXZ, new Vector2(entity.WorldOffset.X, entity.WorldOffset.Z));
        parameters.Set(RiverSurfaceKeys._TerrainNormalStepSize, Vector2.One);
        var terrainWorldSize = new Vector2(Math.Max(entity.HeightmapWidth - 1, 1), Math.Max(entity.HeightmapHeight - 1, 1));
        var worldSpaceToTerrain = new Vector2(1.0f / terrainWorldSize.X, 1.0f / terrainWorldSize.Y);
        parameters.Set(RiverSurfaceKeys._WorldSpaceToTerrain0To1, worldSpaceToTerrain);
        parameters.Set(RiverSurfaceKeys._InverseWorldSize, worldSpaceToTerrain);
        return true;
    }

    private static void SetEditorTerrainSliceTextures(ParameterCollection parameters, EditorTerrainEntity entity, EditorTerrainRenderObject renderObject)
    {
        var firstSliceTexture = renderObject.HeightmapSliceTextures[0] ?? entity.Slices[0].Texture;
        SetEditorTerrainSliceTexture(parameters, 0, renderObject.HeightmapSliceTextures[0] ?? firstSliceTexture);
        SetEditorTerrainSliceTexture(parameters, 1, renderObject.HeightmapSliceTextures[1] ?? firstSliceTexture);
        SetEditorTerrainSliceTexture(parameters, 2, renderObject.HeightmapSliceTextures[2] ?? firstSliceTexture);
        SetEditorTerrainSliceTexture(parameters, 3, renderObject.HeightmapSliceTextures[3] ?? firstSliceTexture);
        SetEditorTerrainSliceTexture(parameters, 4, renderObject.HeightmapSliceTextures[4] ?? firstSliceTexture);
        SetEditorTerrainSliceTexture(parameters, 5, renderObject.HeightmapSliceTextures[5] ?? firstSliceTexture);
        SetEditorTerrainSliceTexture(parameters, 6, renderObject.HeightmapSliceTextures[6] ?? firstSliceTexture);
        SetEditorTerrainSliceTexture(parameters, 7, renderObject.HeightmapSliceTextures[7] ?? firstSliceTexture);
    }

    private static void SetEditorTerrainSliceTexture(ParameterCollection parameters, int sliceIndex, Texture texture)
    {
        switch (sliceIndex)
        {
            case 0: parameters.Set(RiverSurfaceKeys.HeightmapSlice0, texture); break;
            case 1: parameters.Set(RiverSurfaceKeys.HeightmapSlice1, texture); break;
            case 2: parameters.Set(RiverSurfaceKeys.HeightmapSlice2, texture); break;
            case 3: parameters.Set(RiverSurfaceKeys.HeightmapSlice3, texture); break;
            case 4: parameters.Set(RiverSurfaceKeys.HeightmapSlice4, texture); break;
            case 5: parameters.Set(RiverSurfaceKeys.HeightmapSlice5, texture); break;
            case 6: parameters.Set(RiverSurfaceKeys.HeightmapSlice6, texture); break;
            case 7: parameters.Set(RiverSurfaceKeys.HeightmapSlice7, texture); break;
        }
    }

    private static void SetEditorTerrainSliceBounds(ParameterCollection parameters, EditorTerrainEntity entity)
    {
        SetEditorTerrainSliceBounds(parameters, 0, GetEditorTerrainSliceBounds(entity, 0));
        SetEditorTerrainSliceBounds(parameters, 1, GetEditorTerrainSliceBounds(entity, 1));
        SetEditorTerrainSliceBounds(parameters, 2, GetEditorTerrainSliceBounds(entity, 2));
        SetEditorTerrainSliceBounds(parameters, 3, GetEditorTerrainSliceBounds(entity, 3));
        SetEditorTerrainSliceBounds(parameters, 4, GetEditorTerrainSliceBounds(entity, 4));
        SetEditorTerrainSliceBounds(parameters, 5, GetEditorTerrainSliceBounds(entity, 5));
        SetEditorTerrainSliceBounds(parameters, 6, GetEditorTerrainSliceBounds(entity, 6));
        SetEditorTerrainSliceBounds(parameters, 7, GetEditorTerrainSliceBounds(entity, 7));
    }

    private static Vector4 GetEditorTerrainSliceBounds(EditorTerrainEntity entity, int sliceIndex)
    {
        if (sliceIndex >= entity.Slices.Count)
        {
            return new Vector4(0, 0, 1, 1);
        }

        var slice = entity.Slices[sliceIndex];
        return new Vector4(slice.StartSampleX, slice.StartSampleZ, slice.Width, slice.Height);
    }

    private static void SetEditorTerrainSliceBounds(ParameterCollection parameters, int sliceIndex, Vector4 bounds)
    {
        switch (sliceIndex)
        {
            case 0: parameters.Set(RiverSurfaceKeys.HeightmapSliceBounds0, bounds); break;
            case 1: parameters.Set(RiverSurfaceKeys.HeightmapSliceBounds1, bounds); break;
            case 2: parameters.Set(RiverSurfaceKeys.HeightmapSliceBounds2, bounds); break;
            case 3: parameters.Set(RiverSurfaceKeys.HeightmapSliceBounds3, bounds); break;
            case 4: parameters.Set(RiverSurfaceKeys.HeightmapSliceBounds4, bounds); break;
            case 5: parameters.Set(RiverSurfaceKeys.HeightmapSliceBounds5, bounds); break;
            case 6: parameters.Set(RiverSurfaceKeys.HeightmapSliceBounds6, bounds); break;
            case 7: parameters.Set(RiverSurfaceKeys.HeightmapSliceBounds7, bounds); break;
        }
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
        blendState.RenderTargets[0].AlphaSourceBlend = Blend.One;
        blendState.RenderTargets[0].AlphaDestinationBlend = Blend.Zero;
        return blendState;
    }

    private static BlendStateDescription CreateSurfaceBlendState()
    {
        var blendState = BlendStateDescription.Default;
        blendState.RenderTargets[0].BlendEnable = true;
        blendState.RenderTargets[0].ColorSourceBlend = Blend.SourceAlpha;
        blendState.RenderTargets[0].ColorDestinationBlend = Blend.InverseSourceAlpha;
        blendState.RenderTargets[0].AlphaSourceBlend = Blend.SourceAlpha;
        blendState.RenderTargets[0].AlphaDestinationBlend = Blend.InverseSourceAlpha;
        blendState.RenderTargets[0].ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue;
        return blendState;
    }

    private static MutablePipelineState CreatePipelineState(GraphicsDevice graphicsDevice, BlendStateDescription blendState, DepthStencilStateDescription depthStencilState)
    {
        var pipelineState = new MutablePipelineState(graphicsDevice);
        pipelineState.State.PrimitiveType = PrimitiveType.TriangleList;
        pipelineState.State.InputElements = RiverInputElements;
        pipelineState.State.BlendState = blendState;
        pipelineState.State.DepthStencilState = depthStencilState;
        pipelineState.State.RasterizerState = CreateRasterizerState(wireframe: false);
        return pipelineState;
    }

    private static RasterizerStateDescription CreateRasterizerState(bool wireframe)
    {
        return new RasterizerStateDescription(CullMode.None)
        {
            FillMode = wireframe ? FillMode.Wireframe : FillMode.Solid,
            DepthBias = -1,
            SlopeScaleDepthBias = -1.0f,
        };
    }
}
