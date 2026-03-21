#nullable enable

using System;
using System.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;

namespace Terrain;

internal sealed class TerrainComputeDispatcher : IDisposable
{
    private const int ComputeThreadCountX = 64;

    private ComputeEffectShader? buildLodMapEffect;
    private ComputeEffectShader? buildNeighborMaskEffect;

    public void Initialize(RenderContext renderContext)
    {
        buildLodMapEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildLodMap",
            ThreadNumbers = new Int3(ComputeThreadCountX, 1, 1),
        };

        buildNeighborMaskEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildNeighborMask",
            ThreadNumbers = new Int3(ComputeThreadCountX, 1, 1),
        };
    }

    public void Dispatch(RenderDrawContext drawContext, TerrainRenderObject renderObject, int instanceCount)
    {
        Debug.Assert(buildLodMapEffect != null);
        Debug.Assert(buildNeighborMaskEffect != null);
        Debug.Assert(renderObject.InstanceBuffer != null);
        Debug.Assert(renderObject.LodMapTexture != null);

        if (instanceCount <= 0)
        {
            return;
        }

        int threadGroupCountX = (instanceCount + ComputeThreadCountX - 1) / ComputeThreadCountX;
        drawContext.CommandList.ResourceBarrierTransition(renderObject.InstanceBuffer, GraphicsResourceState.NonPixelShaderResource);
        drawContext.CommandList.ResourceBarrierTransition(renderObject.LodMapTexture, GraphicsResourceState.UnorderedAccess);

        buildLodMapEffect!.ThreadGroupCounts = new Int3(threadGroupCountX, 1, 1);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.InstanceBuffer, renderObject.InstanceBuffer);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMap, renderObject.LodMapTexture);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.InstanceCount, instanceCount);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMapWidth, renderObject.LodMapTexture.Width);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMapHeight, renderObject.LodMapTexture.Height);
        buildLodMapEffect.Draw(drawContext);

        drawContext.CommandList.ResourceBarrierTransition(renderObject.LodMapTexture, GraphicsResourceState.NonPixelShaderResource);
        drawContext.CommandList.ResourceBarrierTransition(renderObject.InstanceBuffer, GraphicsResourceState.UnorderedAccess);

        buildNeighborMaskEffect!.ThreadGroupCounts = new Int3(threadGroupCountX, 1, 1);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.InstanceBuffer, renderObject.InstanceBuffer);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMap, renderObject.LodMapTexture);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.InstanceCount, instanceCount);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMapWidth, renderObject.LodMapTexture.Width);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMapHeight, renderObject.LodMapTexture.Height);
        buildNeighborMaskEffect.Draw(drawContext);

        drawContext.CommandList.ResourceBarrierTransition(renderObject.InstanceBuffer, GraphicsResourceState.NonPixelShaderResource);
    }

    public void Dispose()
    {
        buildLodMapEffect?.Dispose();
        buildLodMapEffect = null;

        buildNeighborMaskEffect?.Dispose();
        buildNeighborMaskEffect = null;
    }
}
