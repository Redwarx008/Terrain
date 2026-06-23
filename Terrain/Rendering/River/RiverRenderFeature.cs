#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using Terrain.Rendering.Water;

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
    private DynamicEffectInstance? bottomEffect;
    private DynamicEffectInstance? surfaceEffect;
    private MutablePipelineState? bottomPipelineState;
    private MutablePipelineState? surfacePipelineState;
    private readonly RiverRenderResources renderResources = new();
    private readonly RiverResourceLoader riverResources = new();
    private ContentManager? contentManager;
    private ForwardLightingRenderFeature? forwardLightingFeature;
    private IShadowMapRenderer? bottomShadowMapRenderer;
    private WaterSceneLightingBinder? sceneLightingBinder;

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
        sceneLightingBinder = new WaterSceneLightingBinder(this, forwardLightingFeature, bottomShadowMapRenderer);

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
        forwardLightingFeature = null;
        bottomShadowMapRenderer = null;
        sceneLightingBinder = null;
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
        // River is driven by CustomForwardRenderer so it can consume the shared
        // water refraction capture. If a Transparent selector is accidentally
        // left in a compositor, keep this callback inert to avoid a double draw.
        return;
    }

    internal void DrawWaterChain(
        RenderDrawContext context,
        RenderView renderView,
        RenderViewStage renderViewStage,
        int startIndex,
        int endIndex,
        Texture sharedRefractionTexture,
        int refractionWidth,
        int refractionHeight)
    {
        if (bottomEffect == null
            || surfaceEffect == null
            || bottomPipelineState == null
            || surfacePipelineState == null
            || startIndex >= endIndex
            || sharedRefractionTexture == null
            || refractionWidth <= 0
            || refractionHeight <= 0)
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

        renderResources.EnsureResourcesForCaptureSize(graphicsDevice, refractionWidth, refractionHeight);
        if (renderResources.BottomColor == null || renderResources.BottomDepth == null)
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

        sceneLightingBinder?.Bind(context, renderView, bottomEffect, surfaceEffect);
        ApplyDebugRasterizerState(bottomPipelineState, isSurface: false, nearClipPlane: renderView.NearClipPlane);
        ApplyDebugRasterizerState(surfacePipelineState, isSurface: true, nearClipPlane: renderView.NearClipPlane);

        CopyRefractionCaptureToBottomColor(commandList, sharedRefractionTexture);

        if (DebugMode != RiverRenderDebugMode.SurfaceOnly)
        {
            using (context.PushRenderTargetsAndRestore())
            {
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
            && sourceSettings.SeaLevel == candidateSettings.SeaLevel
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

    internal float GetRefractionMaxCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        return ResolveRefractionMaxCameraHeight(renderViewStage, startIndex, endIndex);
    }

    internal float GetRiverMaxVisibleCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        return ResolveRiverMaxVisibleCameraHeight(renderViewStage, startIndex, endIndex);
    }

    private float ResolveRefractionMaxCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        float maxHeight = 50.0f;
        for (int index = startIndex; index < endIndex; index++)
        {
            if (renderViewStage.SortedRenderNodes[index].RenderObject is RiverRenderObject riverObject && riverObject.Enabled)
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
            if (renderViewStage.SortedRenderNodes[index].RenderObject is RiverRenderObject riverObject && riverObject.Enabled)
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

    private void CopyRefractionCaptureToBottomColor(CommandList commandList, Texture sharedRefractionTexture)
    {
        if (renderResources.BottomColor == null)
        {
            return;
        }

        commandList.CopyRegion(sharedRefractionTexture, 0, null, renderResources.BottomColor, 0);
    }

    private static void ApplyBottomParameters(DynamicEffectInstance effect, RiverRenderObject riverObject, RiverRenderSettings settings)
    {
        effect.Parameters.Set(RiverBottomKeys._MapExtent, riverObject.MapExtent);
        effect.Parameters.Set(RiverBottomKeys._WaterHeight, settings.SeaLevel);
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
        effect.Parameters.Set(RiverSurfaceKeys._WaterHeight, settings.SeaLevel);
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
            if (renderViewStage.SortedRenderNodes[index].RenderObject is not RiverRenderObject riverObject
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
