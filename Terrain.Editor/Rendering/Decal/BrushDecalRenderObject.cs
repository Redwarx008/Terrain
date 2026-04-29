#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Graphics.GeometricPrimitives;
using Stride.Rendering;

namespace Terrain.Editor.Rendering.Decal;

/// <summary>
/// Render object for the brush decal.
/// Holds world matrix, color, texture scale, and the cube geometric primitive
/// used for screen-space decal projection.
/// </summary>
public sealed class BrushDecalRenderObject : RenderObject, IDisposable
{
    public Color4 Color = Color4.White;
    public float TextureScale = 1f;
    public Matrix WorldMatrix = Matrix.Identity;
    public GeometricPrimitive? RenderCube;

    public void Prepare(GraphicsDevice graphicsDevice)
    {
        if (RenderCube != null)
        {
            return;
        }

        RenderCube = GeometricPrimitive.Cube.New(graphicsDevice);
        var pipelineState = RenderCube.PipelineState.State;
        pipelineState.BlendState = BlendStates.AlphaBlend;
        pipelineState.DepthStencilState = DepthStencilStates.DepthRead;
        pipelineState.RasterizerState = new RasterizerStateDescription(CullMode.Back)
        {
            DepthBias = -10,
            SlopeScaleDepthBias = -1.0f,
        };
    }

    public void Dispose()
    {
        RenderCube?.Dispose();
        RenderCube = null;
    }
}
