#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using Terrain.Rendering.Water;

namespace Terrain.Rendering.Ocean;

public sealed class OceanRenderFeature : RootRenderFeature
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");
    private static readonly InputElementDescription[] OceanInputElements = OceanVertex.Layout.CreateInputElements();

    private DynamicEffectInstance? oceanEffect;
    private MutablePipelineState? pipelineState;
    private readonly OceanResourceLoader oceanResources = new();
    private ForwardLightingRenderFeature? forwardLightingFeature;
    private IShadowMapRenderer? shadowMapRenderer;
    private WaterSceneLightingBinder? sceneLightingBinder;

    public override Type SupportedRenderObjectType => typeof(OceanRenderObject);

    public OceanRenderFeature()
    {
        SortKey = 180;
    }

    protected override void InitializeCore()
    {
        base.InitializeCore();

        oceanEffect = new DynamicEffectInstance("OceanSurface");
        oceanEffect.Initialize(Context.Services);

        try
        {
            oceanResources.Load(Context.GraphicsDevice);
        }
        catch (Exception exception)
        {
            Log.Error($"Ocean render resources could not be loaded: {exception.Message}");
            oceanResources.Dispose();
        }

        BindStaticResources(Context.GraphicsDevice);

        var meshRenderFeature = RenderSystem?.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
        forwardLightingFeature = meshRenderFeature?.RenderFeatures.OfType<ForwardLightingRenderFeature>().FirstOrDefault();
        shadowMapRenderer = forwardLightingFeature?.ShadowMapRenderer;
        sceneLightingBinder = new WaterSceneLightingBinder(this, forwardLightingFeature, shadowMapRenderer);

        pipelineState = CreatePipelineState(Context.GraphicsDevice);
    }

    protected override void Destroy()
    {
        oceanResources.Dispose();
        oceanEffect?.Dispose();
        oceanEffect = null;
        pipelineState = null;
        forwardLightingFeature = null;
        shadowMapRenderer = null;
        sceneLightingBinder = null;

        base.Destroy();
    }

    public override void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);

        if (oceanEffect == null || !oceanResources.IsLoaded)
        {
            return;
        }

        var oceanParametersSource = FindFirstPreparedOceanObject();
        if (oceanParametersSource == null)
        {
            return;
        }

        var material = GetOceanMaterial(oceanParametersSource);
        if (material == null)
        {
            return;
        }

        DebugAssertPreparedOceanParametersMatch(oceanParametersSource, material);
        ApplyOceanParameters(oceanEffect, oceanParametersSource, material);
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        // Ocean is driven by CustomForwardRenderer and the dedicated Water
        // stage so it can consume the shared refraction capture. Keep this
        // stage callback inert to avoid a selector residue double draw.
        return;
    }

    internal void DrawWater(
        RenderDrawContext context,
        RenderView renderView,
        RenderViewStage renderViewStage,
        int startIndex,
        int endIndex,
        Texture sharedRefractionTexture,
        int refractionWidth,
        int refractionHeight,
        float refractionMaxCameraHeight)
    {
        if (oceanEffect == null || pipelineState == null || !oceanResources.IsLoaded || startIndex >= endIndex)
        {
            return;
        }

        if (sharedRefractionTexture == null || refractionWidth <= 0 || refractionHeight <= 0)
        {
            return;
        }

        Matrix.Invert(ref renderView.View, out var viewInverse);
        var cameraWorldPosition = viewInverse.TranslationVector;
        float waterFlowTime = (float)context.RenderContext.Time.Total.TotalSeconds;
        oceanEffect.Parameters.Set(OceanSurfaceKeys._CameraWorldPosition, cameraWorldPosition);
        oceanEffect.Parameters.Set(OceanSurfaceKeys._GlobalTime, waterFlowTime);
        oceanEffect.Parameters.Set(OceanSurfaceKeys._WaterFlowTime, waterFlowTime);
        oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionTexture, sharedRefractionTexture);
        oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionSampler, context.GraphicsDevice.SamplerStates.LinearClamp);
        oceanEffect.Parameters.Set(OceanSurfaceKeys._RefractionTextureSize, new Vector2(refractionWidth, refractionHeight));
        oceanEffect.Parameters.Set(RiverCommonKeys._RefractionMaxCameraHeight, refractionMaxCameraHeight);
        ApplyViewParameters(oceanEffect, renderView);
        sceneLightingBinder?.Bind(context, renderView, oceanEffect);

        var graphicsContext = context.GraphicsContext;
        var commandList = context.CommandList;
        pipelineState.State.RootSignature = oceanEffect.RootSignature;
        pipelineState.State.EffectBytecode = oceanEffect.Effect.Bytecode;
        pipelineState.State.InputElements = OceanInputElements;
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
            if (renderNode.RenderObject is not OceanRenderObject oceanObject
                || !oceanObject.Enabled
                || oceanObject.MeshDraw == null
                || oceanObject.IndexBuffer == null
                || oceanObject.VertexBuffer == null)
            {
                continue;
            }

            oceanEffect.Parameters.Set(TransformationKeys.World, oceanObject.World);
            oceanEffect.Parameters.Set(TransformationKeys.WorldView, oceanObject.World * renderView.View);
            oceanEffect.Parameters.Set(TransformationKeys.WorldViewProjection, oceanObject.World * renderView.ViewProjection);

            oceanEffect.Apply(graphicsContext);
            commandList.SetVertexBuffer(0, oceanObject.VertexBuffer, 0, OceanVertex.Layout.VertexStride);
            commandList.SetIndexBuffer(oceanObject.IndexBuffer, 0, true);
            commandList.DrawIndexed(oceanObject.IndexCount);
        }
    }

    private OceanRenderObject? FindFirstPreparedOceanObject()
    {
        foreach (var renderObject in RenderObjects)
        {
            if (renderObject is OceanRenderObject oceanObject
                && oceanObject.Enabled
                && oceanObject.MeshDraw != null
                && oceanObject.IndexBuffer != null
                && oceanObject.VertexBuffer != null)
            {
                return oceanObject;
            }
        }

        return null;
    }

    private static OceanMaterialSettings? GetOceanMaterial(OceanRenderObject oceanObject)
    {
        return (oceanObject.Source as OceanComponent)?.Material;
    }

    [Conditional("DEBUG")]
    private void DebugAssertPreparedOceanParametersMatch(OceanRenderObject source, OceanMaterialSettings sourceMaterial)
    {
        foreach (var renderObject in RenderObjects)
        {
            if (renderObject is not OceanRenderObject oceanObject
                || ReferenceEquals(oceanObject, source)
                || !oceanObject.Enabled
                || oceanObject.MeshDraw == null
                || oceanObject.IndexBuffer == null
                || oceanObject.VertexBuffer == null)
            {
                continue;
            }

            var candidateMaterial = GetOceanMaterial(oceanObject);
            Debug.Assert(
                candidateMaterial != null && OceanParametersMatch(source, sourceMaterial, oceanObject, candidateMaterial),
                "OceanRenderFeature binds non-frame ocean shader parameters once during Prepare; all drawable ocean objects must share those parameters.");
        }
    }

    private static bool OceanParametersMatch(
        OceanRenderObject source,
        OceanMaterialSettings sourceMaterial,
        OceanRenderObject candidate,
        OceanMaterialSettings candidateMaterial)
    {
        return source.SeaLevel == candidate.SeaLevel
            && source.MapWorldSize == candidate.MapWorldSize
            && sourceMaterial.ShallowColor == candidateMaterial.ShallowColor
            && sourceMaterial.DeepColor == candidateMaterial.DeepColor
            && sourceMaterial.Roughness == candidateMaterial.Roughness
            && sourceMaterial.WaveScale == candidateMaterial.WaveScale;
    }

    private static void ApplyOceanParameters(DynamicEffectInstance effect, OceanRenderObject oceanObject, OceanMaterialSettings material)
    {
        effect.Parameters.Set(OceanSurfaceKeys.ShallowColor, ToVector4(material.ShallowColor));
        effect.Parameters.Set(OceanSurfaceKeys.DeepColor, ToVector4(material.DeepColor));
        effect.Parameters.Set(OceanSurfaceKeys._OceanRoughness, material.Roughness);
        effect.Parameters.Set(OceanSurfaceKeys._WaveScale, material.WaveScale);
        effect.Parameters.Set(OceanSurfaceKeys._WaterHeight, oceanObject.SeaLevel);
        effect.Parameters.Set(OceanSurfaceKeys._MapWorldSize, oceanObject.MapWorldSize);
    }

    private static void ApplyViewParameters(DynamicEffectInstance effect, RenderView renderView)
    {
        effect.Parameters.Set(TransformationKeys.ViewProjection, renderView.ViewProjection);
        effect.Parameters.Set(CameraKeys.ViewSize, renderView.ViewSize);
        effect.Parameters.Set(OceanSurfaceKeys._ViewSize, renderView.ViewSize);
        effect.Parameters.Set(OceanSurfaceKeys._ViewMatrix, renderView.View);
    }

    private void BindStaticResources(GraphicsDevice graphicsDevice)
    {
        if (oceanEffect == null)
        {
            return;
        }

        oceanEffect.Parameters.Set(OceanSurfaceKeys.OceanTextureSampler, graphicsDevice.SamplerStates.LinearWrap);
        oceanEffect.Parameters.Set(RiverStrideLightingKeys.EnvironmentMapSampler, graphicsDevice.SamplerStates.LinearClamp);
        SetTexture(oceanEffect.Parameters, OceanSurfaceKeys.WaterColorTexture, oceanResources.WaterColor);
        SetTexture(oceanEffect.Parameters, OceanSurfaceKeys.AmbientNormalTexture, oceanResources.AmbientNormal);
        SetTexture(oceanEffect.Parameters, OceanSurfaceKeys.FlowMapTexture, oceanResources.FlowMap);
        if (oceanResources.FlowMap != null)
        {
            oceanEffect.Parameters.Set(OceanSurfaceKeys._WaterFlowMapSize, new Vector2(oceanResources.FlowMap.ViewWidth, oceanResources.FlowMap.ViewHeight));
        }
        SetTexture(oceanEffect.Parameters, OceanSurfaceKeys.FlowNormalTexture, oceanResources.FlowNormal);
        SetTexture(oceanEffect.Parameters, OceanSurfaceKeys.FoamTexture, oceanResources.Foam);
        SetTexture(oceanEffect.Parameters, OceanSurfaceKeys.FoamRampTexture, oceanResources.FoamRamp);
        SetTexture(oceanEffect.Parameters, OceanSurfaceKeys.FoamMapTexture, oceanResources.FoamMap);
        SetTexture(oceanEffect.Parameters, OceanSurfaceKeys.FoamNoiseTexture, oceanResources.FoamNoise);
    }

    private static void SetTexture(ParameterCollection parameters, ObjectParameterKey<Texture> key, Texture? texture)
    {
        parameters.SetObject(key, texture);
    }

    private static MutablePipelineState CreatePipelineState(GraphicsDevice graphicsDevice)
    {
        var pipelineState = new MutablePipelineState(graphicsDevice);
        pipelineState.State.PrimitiveType = PrimitiveType.TriangleList;
        pipelineState.State.InputElements = OceanInputElements;
        pipelineState.State.BlendState = CreateBlendState();
        pipelineState.State.DepthStencilState = CreateDepthStencilState();
        pipelineState.State.RasterizerState = new RasterizerStateDescription(CullMode.None);
        return pipelineState;
    }

    private static BlendStateDescription CreateBlendState()
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

    private static DepthStencilStateDescription CreateDepthStencilState()
    {
        return new DepthStencilStateDescription(depthEnable: true, depthWriteEnable: false)
        {
            DepthBufferFunction = CompareFunction.Less,
        };
    }

    private static Vector4 ToVector4(Color3 color)
    {
        return new Vector4(color.R, color.G, color.B, 1.0f);
    }
}
