#nullable enable

using System;
using System.Diagnostics;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;

namespace Terrain;

internal sealed class TerrainComputeDispatcher : IDisposable
{
    private const int LookupThreadCountX = 64;
    private const int LodMapThreadCountX = 8;
    private const int LodMapThreadCountY = 8;
    private static readonly ProfilingKey BuildLodLookupKey = new("Terrain.BuildLodLookup");
    private static readonly ProfilingKey BuildLodMapKey = new("Terrain.BuildLodMap");
    private static readonly ProfilingKey BuildNeighborMaskKey = new("Terrain.BuildNeighborMask");

    private ComputeEffectShader? buildLodLookupEffect;
    private ComputeEffectShader? buildLodMapEffect;
    private ComputeEffectShader? buildNeighborMaskEffect;

    public void Initialize(RenderContext renderContext)
    {
        buildLodLookupEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildLodLookup",
            ThreadNumbers = new Int3(LookupThreadCountX, 1, 1),
        };

        buildLodMapEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildLodMap",
            ThreadNumbers = new Int3(LodMapThreadCountX, LodMapThreadCountY, 1),
        };

        buildNeighborMaskEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildNeighborMask",
            ThreadNumbers = new Int3(LookupThreadCountX, 1, 1),
        };
    }

    public void Dispatch(RenderDrawContext drawContext, TerrainRenderObject renderObject, int renderCount, int nodeCount, int maxLod)
    {
        Debug.Assert(buildLodLookupEffect != null);
        Debug.Assert(buildLodMapEffect != null);
        Debug.Assert(buildNeighborMaskEffect != null);
        Debug.Assert(renderObject.ChunkNodeBuffer != null);
        Debug.Assert(renderObject.LodLookupBuffer != null);
        Debug.Assert(renderObject.LodLookupLayoutBuffer != null);
        Debug.Assert(renderObject.LodMapTexture != null);

        if (renderCount <= 0 || nodeCount <= 0)
        {
            return;
        }

        int lookupThreadGroupCountX = (nodeCount + LookupThreadCountX - 1) / LookupThreadCountX;
        int neighborThreadGroupCountX = (renderCount + LookupThreadCountX - 1) / LookupThreadCountX;
        int lodMapThreadGroupCountX = (renderObject.LodMapTexture.Width + LodMapThreadCountX - 1) / LodMapThreadCountX;
        int lodMapThreadGroupCountY = (renderObject.LodMapTexture.Height + LodMapThreadCountY - 1) / LodMapThreadCountY;
        var commandList = drawContext.CommandList;

        commandList.ResourceBarrierTransition(renderObject.ChunkNodeBuffer, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(renderObject.LodLookupLayoutBuffer, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(renderObject.LodLookupBuffer, GraphicsResourceState.UnorderedAccess);
        commandList.ResourceBarrierTransition(renderObject.LodMapTexture, GraphicsResourceState.UnorderedAccess);

        using (Profiler.Begin(BuildLodLookupKey))
        {
            buildLodLookupEffect!.ThreadGroupCounts = new Int3(lookupThreadGroupCountX, 1, 1);
            buildLodLookupEffect.Parameters.Set(TerrainBuildLodLookupKeys.ChunkNodeBuffer, renderObject.ChunkNodeBuffer);
            buildLodLookupEffect.Parameters.Set(TerrainBuildLodLookupKeys.LodLookupLayoutBuffer, renderObject.LodLookupLayoutBuffer);
            buildLodLookupEffect.Parameters.Set(TerrainBuildLodLookupKeys.LodLookupBuffer, renderObject.LodLookupBuffer);
            buildLodLookupEffect.Parameters.Set(TerrainBuildLodLookupKeys.NodeCount, nodeCount);
            buildLodLookupEffect.Draw(drawContext);
        }

        commandList.ResourceBarrierTransition(renderObject.LodLookupBuffer, GraphicsResourceState.NonPixelShaderResource);

        using (Profiler.Begin(BuildLodMapKey))
        {
            buildLodMapEffect!.ThreadGroupCounts = new Int3(lodMapThreadGroupCountX, lodMapThreadGroupCountY, 1);
            buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodLookupBuffer, renderObject.LodLookupBuffer);
            buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodLookupLayoutBuffer, renderObject.LodLookupLayoutBuffer);
            buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMap, renderObject.LodMapTexture);
            buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.MaxLod, maxLod);
            buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMapWidth, renderObject.LodMapTexture.Width);
            buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMapHeight, renderObject.LodMapTexture.Height);
            buildLodMapEffect.Draw(drawContext);
        }

        commandList.ResourceBarrierTransition(renderObject.LodMapTexture, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(renderObject.ChunkNodeBuffer, GraphicsResourceState.UnorderedAccess);

        using (Profiler.Begin(BuildNeighborMaskKey))
        {
            buildNeighborMaskEffect!.ThreadGroupCounts = new Int3(neighborThreadGroupCountX, 1, 1);
            buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.InstanceBuffer, renderObject.ChunkNodeBuffer);
            buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMap, renderObject.LodMapTexture);
            buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.InstanceCount, renderCount);
            buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMapWidth, renderObject.LodMapTexture.Width);
            buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMapHeight, renderObject.LodMapTexture.Height);
            buildNeighborMaskEffect.Draw(drawContext);
        }

        commandList.ResourceBarrierTransition(renderObject.ChunkNodeBuffer, GraphicsResourceState.NonPixelShaderResource);
    }

    public void Dispose()
    {
        buildLodLookupEffect?.Dispose();
        buildLodLookupEffect = null;

        buildLodMapEffect?.Dispose();
        buildLodMapEffect = null;

        buildNeighborMaskEffect?.Dispose();
        buildNeighborMaskEffect = null;
    }
}
