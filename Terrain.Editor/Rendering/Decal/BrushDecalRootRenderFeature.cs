#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Streaming;

namespace Terrain.Editor.Rendering.Decal;

/// <summary>
/// Root render feature for brush decal rendering.
/// Implements screen-space decal projection: draws a cube into the scene,
/// reads the depth buffer in the pixel shader, reconstructs world position,
/// and projects the decal circle onto the terrain surface.
/// </summary>
public class BrushDecalRootRenderFeature : RootRenderFeature
{
    private DynamicEffectInstance? _decalShader;

    public override Type SupportedRenderObjectType => typeof(BrushDecalRenderObject);

    public BrushDecalRootRenderFeature()
    {
        SortKey = 200;
    }

    protected override void InitializeCore()
    {
        base.InitializeCore();

        _decalShader = new DynamicEffectInstance("BrushDecalShader");
        _decalShader.Initialize(Context.Services);
    }

    protected override void Destroy()
    {
        foreach (var renderObject in RenderObjects)
        {
            if (renderObject is BrushDecalRenderObject decalRenderObject)
            {
                decalRenderObject.Dispose();
            }
        }

        _decalShader?.Dispose();
        _decalShader = null;

        base.Destroy();
    }

    public override void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);

        foreach (var renderObject in RenderObjects)
        {
            var decalRenderObj = (BrushDecalRenderObject)renderObject;
            decalRenderObj.Prepare(context.GraphicsDevice);
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        if (_decalShader == null)
        {
            return;
        }

        var graphicsDevice = context.GraphicsDevice;
        var graphicsContext = context.GraphicsContext;
        var commandList = graphicsContext.CommandList;

        // Refresh shader, might have changed during runtime
        _decalShader.UpdateEffect(graphicsDevice);

        // Set common shader parameters
        _decalShader.Parameters.Set(TransformationKeys.ViewProjection, renderView.ViewProjection);
        _decalShader.Parameters.Set(TransformationKeys.ViewInverse, Matrix.Invert(renderView.View));

        // Resolve the depth buffer as a shader resource.
        // Must use the Presenter's depth buffer for correct behavior in the editor.
        var depthStencil = context.Resolver.ResolveDepthStencil(graphicsDevice.Presenter.DepthStencilBuffer);
        if (depthStencil == null)
        {
            return;
        }

        _decalShader.Parameters.Set(DepthBaseKeys.DepthStencil, depthStencil);

        _decalShader.Parameters.Set(CameraKeys.ViewSize, renderView.ViewSize);
        _decalShader.Parameters.Set(CameraKeys.ZProjection, CameraKeys.ZProjectionACalculate(renderView.NearClipPlane, renderView.FarClipPlane));
        _decalShader.Parameters.Set(CameraKeys.NearClipPlane, renderView.NearClipPlane);
        _decalShader.Parameters.Set(CameraKeys.FarClipPlane, renderView.FarClipPlane);

        for (int index = startIndex; index < endIndex; index++)
        {
            var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
            var renderNode = GetRenderNode(renderNodeReference);
            var decalRenderObj = (BrushDecalRenderObject)renderNode.RenderObject;

            if (decalRenderObj.RenderCube == null)
            {
                continue;
            }

            // Assign per-object shader parameters
            _decalShader.Parameters.Set(TransformationKeys.WorldInverse, Matrix.Invert(decalRenderObj.WorldMatrix));
            _decalShader.Parameters.Set(TransformationKeys.WorldViewProjection, decalRenderObj.WorldMatrix * renderView.ViewProjection);
            _decalShader.Parameters.Set(TransformationKeys.WorldView, decalRenderObj.WorldMatrix * renderView.View);
            _decalShader.Parameters.Set(BrushDecalShaderKeys.DecalColor, decalRenderObj.Color);
            _decalShader.Parameters.Set(BrushDecalShaderKeys.TextureScale, decalRenderObj.TextureScale);

            decalRenderObj.RenderCube.Draw(graphicsContext, _decalShader);
        }

        // Release depth stencil resource view to prevent memory leaks
        context.Resolver.ReleaseDepthStenctilAsShaderResource(depthStencil);
    }
}
