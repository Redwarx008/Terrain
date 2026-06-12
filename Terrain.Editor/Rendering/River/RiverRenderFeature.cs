#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
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

    private DynamicEffectInstance? bottomEffect;
    private DynamicEffectInstance? surfaceEffect;
    private MutablePipelineState? bottomPipelineState;
    private MutablePipelineState? surfacePipelineState;
    private readonly RiverRenderResources renderResources = new();

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

        bottomPipelineState = CreatePipelineState(Context.GraphicsDevice, CreateDualSourceBlendState(), DepthStencilStates.DepthRead);
        surfacePipelineState = CreatePipelineState(Context.GraphicsDevice, BlendStates.AlphaBlend, DepthStencilStates.DepthRead);
    }

    protected override void Destroy()
    {
        renderResources.Dispose();
        bottomEffect?.Dispose();
        bottomEffect = null;
        surfaceEffect?.Dispose();
        surfaceEffect = null;
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
        if (renderResources.BottomColor == null || renderResources.BottomDepth == null)
        {
            return;
        }

        bottomEffect.UpdateEffect(graphicsDevice);
        surfaceEffect.UpdateEffect(graphicsDevice);
        ApplyDebugRasterizerState(bottomPipelineState);
        ApplyDebugRasterizerState(surfacePipelineState);

        if (DebugMode != RiverRenderDebugMode.SurfaceOnly)
        {
            using (context.PushRenderTargetsAndRestore())
            {
                commandList.SetRenderTargetAndViewport(renderResources.BottomDepth, renderResources.BottomColor);
                commandList.Clear(renderResources.BottomColor, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
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
        DrawPass(context, renderView, renderViewStage, startIndex, endIndex, surfaceEffect, surfacePipelineState, renderResources.BottomColor);
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
                effect.Parameters.Set(RiverSurfaceKeys._ViewSize, renderView.ViewSize);
                effect.Parameters.Set(RiverSurfaceKeys._MapExtent, riverObject.MapExtent);
            }
            else if (ReferenceEquals(effect, bottomEffect))
            {
                effect.Parameters.Set(RiverBottomKeys._MapExtent, riverObject.MapExtent);
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

    private static BlendStateDescription CreateDualSourceBlendState()
    {
        var blendState = BlendStateDescription.Default;
        blendState.RenderTargets[0].BlendEnable = true;
        blendState.RenderTargets[0].ColorSourceBlend = Blend.One;
        blendState.RenderTargets[0].ColorDestinationBlend = Blend.InverseSecondarySourceAlpha;
        blendState.RenderTargets[0].AlphaSourceBlend = Blend.One;
        blendState.RenderTargets[0].AlphaDestinationBlend = Blend.InverseSecondarySourceAlpha;
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
