#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terrain.Editor.Rendering;
using Terrain.Editor.Services.PathFeatures;

namespace Terrain.Editor.Services.Commands;

public sealed class PathFeatureEditCommand : ICommand
{
    private readonly PathFeatureService service;
    private readonly TerrainManager terrainManager;
    private readonly PathNetworkSnapshot beforeNetwork;
    private readonly List<HeightChunkDelta> beforeHeightChunks;
    private PathNetworkSnapshot? afterNetwork;
    private List<HeightChunkDelta> afterHeightChunks = new();
    private long estimatedSizeBytes;

    public PathFeatureEditCommand(
        PathFeatureService service,
        TerrainManager terrainManager,
        PathNetworkSnapshot beforeNetwork,
        IReadOnlyList<HeightChunkDelta> beforeHeightChunks,
        string description)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.terrainManager = terrainManager ?? throw new ArgumentNullException(nameof(terrainManager));
        this.beforeNetwork = beforeNetwork.Clone();
        this.beforeHeightChunks = new List<HeightChunkDelta>(beforeHeightChunks);
        Description = description;
    }

    public string Description { get; }

    public long EstimatedSizeBytes => estimatedSizeBytes;

    public TerrainDataChannel AffectedChannel => TerrainDataChannel.Height;

    public void CaptureAfter(PathNetworkSnapshot network, IReadOnlyList<HeightChunkDelta> heightChunks)
    {
        afterNetwork = network.Clone();
        afterHeightChunks = new List<HeightChunkDelta>(heightChunks);
        estimatedSizeBytes = EstimateNetworkSize(beforeNetwork)
            + EstimateNetworkSize(afterNetwork)
            + EstimateHeightSize(beforeHeightChunks)
            + EstimateHeightSize(afterHeightChunks);
    }

    public bool HasChanges()
    {
        if (afterNetwork == null)
            return false;

        if (beforeNetwork.Nodes.Count != afterNetwork.Nodes.Count
            || beforeNetwork.Features.Count != afterNetwork.Features.Count)
        {
            return true;
        }

        if (beforeHeightChunks.Count != afterHeightChunks.Count)
            return true;

        for (int i = 0; i < beforeHeightChunks.Count; i++)
        {
            if (!beforeHeightChunks[i].Data.AsSpan().SequenceEqual(afterHeightChunks[i].Data))
                return true;
        }

        return !AreNetworksEqual(beforeNetwork, afterNetwork);
    }

    public void Execute()
    {
        if (afterNetwork == null)
            return;

        ApplyHeightState(afterHeightChunks);
        service.RestoreSnapshotFromCommand(afterNetwork);
    }

    public void Undo()
    {
        ApplyHeightState(beforeHeightChunks);
        service.RestoreSnapshotFromCommand(beforeNetwork);
    }

    private void ApplyHeightState(IReadOnlyList<HeightChunkDelta> chunks)
    {
        ushort[]? heightData = terrainManager.HeightDataCache;
        if (heightData == null)
            return;

        int dataWidth = terrainManager.HeightCacheWidth;
        foreach (HeightChunkDelta chunk in chunks)
        {
            for (int row = 0; row < chunk.Region.Height; row++)
            {
                int srcOffset = row * chunk.Region.Width;
                int dstOffset = (chunk.Region.Y + row) * dataWidth + chunk.Region.X;
                Array.Copy(chunk.Data, srcOffset, heightData, dstOffset, chunk.Region.Width);
            }

            float centerX = chunk.Region.X + chunk.Region.Width * 0.5f;
            float centerZ = chunk.Region.Y + chunk.Region.Height * 0.5f;
            float radius = Math.Max(chunk.Region.Width, chunk.Region.Height) * 0.5f;
            terrainManager.MarkDataDirty(TerrainDataChannel.Height, (int)centerX, (int)centerZ, radius);
        }
    }

    private static long EstimateNetworkSize(PathNetworkSnapshot snapshot)
    {
        return snapshot.Nodes.Count * 64L + snapshot.Features.Sum(static feature => 128L + feature.NodeIds.Count * 16L);
    }

    private static long EstimateHeightSize(IReadOnlyList<HeightChunkDelta> chunks)
    {
        long total = 0;
        foreach (HeightChunkDelta chunk in chunks)
            total += chunk.Data.Length * sizeof(ushort);
        return total;
    }

    private static bool AreNetworksEqual(PathNetworkSnapshot a, PathNetworkSnapshot b)
    {
        if (a.Nodes.Count != b.Nodes.Count
            || a.Features.Count != b.Features.Count)
        {
            return false;
        }

        var aNodes = a.Nodes.OrderBy(static node => node.Id).ToArray();
        var bNodes = b.Nodes.OrderBy(static node => node.Id).ToArray();
        for (int i = 0; i < aNodes.Length; i++)
        {
            if (aNodes[i].Id != bNodes[i].Id || aNodes[i].Position != bNodes[i].Position)
                return false;
        }

        var aFeatures = a.Features.OrderBy(static feature => feature.Id).ToArray();
        var bFeatures = b.Features.OrderBy(static feature => feature.Id).ToArray();
        for (int i = 0; i < aFeatures.Length; i++)
        {
            if (aFeatures[i].Id != bFeatures[i].Id
                || aFeatures[i].Name != bFeatures[i].Name
                || aFeatures[i].Kind != bFeatures[i].Kind
                || aFeatures[i].Style.Width != bFeatures[i].Style.Width
                || aFeatures[i].Style.Depth != bFeatures[i].Style.Depth
                || aFeatures[i].Style.SideSlope != bFeatures[i].Style.SideSlope
                || aFeatures[i].Style.CornerSpan != bFeatures[i].Style.CornerSpan
                || aFeatures[i].Style.RoadStyle != bFeatures[i].Style.RoadStyle
                || !aFeatures[i].NodeIds.SequenceEqual(bFeatures[i].NodeIds))
            {
                return false;
            }
        }

        return true;
    }
}

public readonly record struct HeightChunkDelta(TerrainChunkRegion Region, ushort[] Data);
