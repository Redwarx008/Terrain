#nullable enable

using System;
using System.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;

namespace Terrain;

internal sealed class TerrainQuadTree : IDisposable
{
    private struct SelectionState
    {
        public Vector3 TerrainOffset;
        public BoundingFrustum Frustum;
        public Vector3 CameraPosition;
        public float ScreenSpaceScale;
        public int Capacity;
        public TerrainChunkNode[] Data;
        public int RenderCount;
        public int SubdividedCount;
        public bool Truncated;
    }

    private readonly TerrainMinMaxErrorMap[] minMaxErrorMaps;
    private readonly int leafChunkSize;
    private readonly int heightmapWidth;
    private readonly int heightmapHeight;
    private readonly int maxLod;
    private readonly int topLevelChunkCountX;
    private readonly int topLevelChunkCountY;
    private readonly TerrainComponent terrain;
    private readonly TerrainStreamingManager streamingManager;

    public TerrainQuadTree(TerrainMinMaxErrorMap[] minMaxErrorMaps, int leafChunkSize, int heightmapWidth, int heightmapHeight, TerrainComponent terrain, TerrainStreamingManager streamingManager)
    {
        Debug.Assert(minMaxErrorMaps.Length > 0);
        Debug.Assert(leafChunkSize > 0);
        Debug.Assert(heightmapWidth > 1);
        Debug.Assert(heightmapHeight > 1);

        this.minMaxErrorMaps = minMaxErrorMaps;
        this.leafChunkSize = leafChunkSize;
        this.heightmapWidth = heightmapWidth;
        this.heightmapHeight = heightmapHeight;
        maxLod = minMaxErrorMaps.Length - 1;
        topLevelChunkCountX = minMaxErrorMaps[maxLod].Width;
        topLevelChunkCountY = minMaxErrorMaps[maxLod].Height;
        this.terrain = terrain;
        this.streamingManager = streamingManager;
    }

    public (int RenderCount, int NodeCount) Select(
        Vector3 terrainOffset,
        RenderView renderView,
        TerrainChunkNode[] chunkNodeData)
    {
        Debug.Assert(chunkNodeData.Length > 0);

        float viewHeight = Math.Max(1.0f, renderView.ViewSize.Y);
        float screenSpaceScale = viewHeight * 0.5f * MathF.Abs(renderView.Projection.M22);
        Matrix.Invert(ref renderView.View, out var viewInverse);
        var selectionState = new SelectionState
        {
            TerrainOffset = terrainOffset,
            Frustum = renderView.Frustum,
            CameraPosition = viewInverse.TranslationVector,
            ScreenSpaceScale = screenSpaceScale,
            Capacity = chunkNodeData.Length,
            Data = chunkNodeData,
            RenderCount = 0,
            SubdividedCount = 0,
            Truncated = false,
        };

        for (int y = 0; y < topLevelChunkCountY; y++)
        {
            for (int x = 0; x < topLevelChunkCountX; x++)
            {
                SelectNode(ref selectionState, x, y, maxLod);
            }
        }

        // Rearrange: subdivided nodes are currently at the end (backwards), move them after render nodes
        int renderCount = selectionState.RenderCount;
        int subdividedCount = selectionState.SubdividedCount;
        int totalNodeCount = renderCount + subdividedCount;

        if (subdividedCount > 0)
        {
            // Subdivided nodes are at indices [capacity - subdividedCount, capacity - 1]
            // Move them to [renderCount, renderCount + subdividedCount - 1]
            int subdividedStart = chunkNodeData.Length - subdividedCount;
            Array.Copy(chunkNodeData, subdividedStart, chunkNodeData, renderCount, subdividedCount);
        }

        return (renderCount, totalNodeCount);
    }

    public void ProcessPendingUploads(CommandList commandList, int maxUploadsPerFrame)
    {
        streamingManager.ProcessPendingUploads(commandList, maxUploadsPerFrame);
    }

    public void Dispose()
    {
        streamingManager.Dispose();
    }

    private void SelectNode(ref SelectionState state, int chunkX, int chunkY, int lodLevel)
    {
        int totalNodeCount = state.RenderCount + state.SubdividedCount;
        if (totalNodeCount >= state.Capacity)
        {
            state.Truncated = true;
            WriteSubdividedNode(ref state, chunkX, chunkY, lodLevel, TerrainLodLookupNodeState.Stop);
            return;
        }

        int sizeInSamples = leafChunkSize << lodLevel;
        int originSampleX = chunkX * sizeInSamples;
        int originSampleY = chunkY * sizeInSamples;
        if (originSampleX >= heightmapWidth - 1 || originSampleY >= heightmapHeight - 1)
        {
            return;
        }

        var minMaxErrorMap = minMaxErrorMaps[lodLevel];
        minMaxErrorMap.Get(chunkX, chunkY, out var minHeight, out var maxHeight, out var geometricError);

        int endSampleX = Math.Min(originSampleX + sizeInSamples, heightmapWidth - 1);
        int endSampleY = Math.Min(originSampleY + sizeInSamples, heightmapHeight - 1);
        float worldHeightScale = terrain.HeightScale * TerrainComponent.HeightSampleNormalization;
        var bounds = new BoundingBox(
            new Vector3(
                state.TerrainOffset.X + originSampleX,
                state.TerrainOffset.Y + minHeight * worldHeightScale,
                state.TerrainOffset.Z + originSampleY),
            new Vector3(
                state.TerrainOffset.X + endSampleX,
                state.TerrainOffset.Y + maxHeight * worldHeightScale,
                state.TerrainOffset.Z + endSampleY));
        var boundsExt = (BoundingBoxExt)bounds;
        if (!state.Frustum.Contains(ref boundsExt))
        {
            return;
        }

        float dx = MathF.Max(MathF.Max(bounds.Minimum.X - state.CameraPosition.X, 0.0f), state.CameraPosition.X - bounds.Maximum.X);
        float dy = MathF.Max(MathF.Max(bounds.Minimum.Y - state.CameraPosition.Y, 0.0f), state.CameraPosition.Y - bounds.Maximum.Y);
        float dz = MathF.Max(MathF.Max(bounds.Minimum.Z - state.CameraPosition.Z, 0.0f), state.CameraPosition.Z - bounds.Maximum.Z);
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        float sse = distance > 1e-4f
            ? state.ScreenSpaceScale * (geometricError * terrain.HeightScale * TerrainComponent.HeightSampleNormalization) / distance
            : float.MaxValue;
        if (lodLevel == 0 || sse <= terrain.MaxScreenSpaceErrorPixels)
        {
            SelectRenderNode(ref state, new TerrainChunkKey(lodLevel, chunkX, chunkY), chunkX, chunkY, lodLevel);
            return;
        }

        var childMap = minMaxErrorMaps[lodLevel - 1];
        childMap.GetSubNodesExist(chunkX, chunkY, out var subTLExist, out var subTRExist, out var subBLExist, out var subBRExist);
        int childChunkX = chunkX * 2;
        int childChunkY = chunkY * 2;
        Span<bool> childExists = stackalloc bool[4] { subTLExist, subTRExist, subBLExist, subBRExist };
        Span<TerrainChunkKey> childKeys = stackalloc TerrainChunkKey[4]
        {
            new TerrainChunkKey(lodLevel - 1, childChunkX, childChunkY),
            new TerrainChunkKey(lodLevel - 1, childChunkX + 1, childChunkY),
            new TerrainChunkKey(lodLevel - 1, childChunkX, childChunkY + 1),
            new TerrainChunkKey(lodLevel - 1, childChunkX + 1, childChunkY + 1),
        };

        bool allChildrenResident = true;
        for (int i = 0; i < childKeys.Length; i++)
        {
            if (childExists[i] && !streamingManager.IsChunkResident(childKeys[i]))
            {
                allChildrenResident = false;
                break;
            }
        }

        if (!allChildrenResident)
        {
            // Keep the parent as the temporary draw node while finer children are still streaming in.
            SelectRenderNode(ref state, new TerrainChunkKey(lodLevel, chunkX, chunkY), chunkX, chunkY, lodLevel);

            for (int i = 0; i < childKeys.Length; i++)
            {
                if (childExists[i])
                {
                    streamingManager.RequestChunk(childKeys[i]);
                }
            }

            return;
        }

        WriteSubdividedNode(ref state, chunkX, chunkY, lodLevel, TerrainLodLookupNodeState.Subdivided);

        if (subTLExist)
        {
            SelectNode(ref state, childChunkX, childChunkY, lodLevel - 1);
        }

        if (subTRExist)
        {
            SelectNode(ref state, childChunkX + 1, childChunkY, lodLevel - 1);
        }

        if (subBLExist)
        {
            SelectNode(ref state, childChunkX, childChunkY + 1, lodLevel - 1);
        }

        if (subBRExist)
        {
            SelectNode(ref state, childChunkX + 1, childChunkY + 1, lodLevel - 1);
        }
    }

    private void SelectRenderNode(ref SelectionState state, TerrainChunkKey key, int chunkX, int chunkY, int lodLevel)
    {
        // Only touch streaming for a node once it is actually selected for drawing or needed as fallback.
        bool isResident = streamingManager.TryGetResidentPageForChunk(key, out int sliceIndex, out int pageOffsetX, out int pageOffsetY, out int pageTexelStride);
        if (!isResident)
        {
            streamingManager.RequestChunk(key);
            WriteSubdividedNode(ref state, chunkX, chunkY, lodLevel, TerrainLodLookupNodeState.Stop);
            return;
        }

        // Render nodes are written from the front
        state.Data[state.RenderCount++] = new TerrainChunkNode
        {
            NodeInfo = new Int4(chunkX, chunkY, lodLevel, (int)TerrainLodLookupNodeState.Stop),
            StreamInfo = new Int4(sliceIndex, pageOffsetX, pageOffsetY, pageTexelStride),
        };
    }

    private void WriteSubdividedNode(ref SelectionState state, int chunkX, int chunkY, int lodLevel, TerrainLodLookupNodeState nodeState)
    {
        // Subdivided nodes are written from the back (to avoid overlapping with render nodes)
        int subdividedIndex = state.Capacity - 1 - state.SubdividedCount;
        if (subdividedIndex < state.RenderCount)
        {
            state.Truncated = true;
            return;
        }

        state.Data[subdividedIndex] = new TerrainChunkNode
        {
            NodeInfo = new Int4(chunkX, chunkY, lodLevel, (int)nodeState),
            StreamInfo = default,
        };
        state.SubdividedCount++;
    }
}
