#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

public sealed class RiverMaskEditCommand : TerrainEditCommand
{
    private readonly Dictionary<long, byte[]> beforeChunkData = new();
    private readonly List<RiverMaskChunkDelta> changedChunks = new();
    private long estimatedSizeBytes;

    public override TerrainDataChannel AffectedChannel => TerrainDataChannel.RiverMask;
    public override string Description => "Paint River";
    public override long EstimatedSizeBytes => estimatedSizeBytes;

    public RiverMaskEditCommand(TerrainManager terrainManager)
        : base(terrainManager)
    {
    }

    protected override int GetDataWidth() => TerrainManager.RiverMap?.Width ?? 0;
    protected override int GetDataHeight() => TerrainManager.RiverMap?.Height ?? 0;

    protected override void CaptureBeforeChunk(TerrainChunkRegion chunk)
    {
        byte[]? riverMaskData = TerrainManager.RiverMap?.GetRawData();
        if (riverMaskData == null || chunk.Width <= 0 || chunk.Height <= 0)
            return;

        beforeChunkData[chunk.Key] = CopyChunk(riverMaskData, chunk);
    }

    protected override bool CaptureAfterStateAndFilter(IReadOnlyList<TerrainChunkRegion> chunks)
    {
        byte[]? riverMaskData = TerrainManager.RiverMap?.GetRawData();
        if (riverMaskData == null)
            return false;

        changedChunks.Clear();
        estimatedSizeBytes = 0;

        foreach (TerrainChunkRegion chunk in chunks)
        {
            if (!beforeChunkData.TryGetValue(chunk.Key, out byte[]? before))
                continue;

            byte[] after = CopyChunk(riverMaskData, chunk);
            if (before.AsSpan().SequenceEqual(after))
                continue;

            changedChunks.Add(new RiverMaskChunkDelta(chunk, before, after));
            estimatedSizeBytes += before.Length + after.Length;
        }

        beforeChunkData.Clear();
        return changedChunks.Count > 0;
    }

    public override void Execute()
    {
        ApplyState(afterState: true);
    }

    public override void Undo()
    {
        ApplyState(afterState: false);
    }

    private void ApplyState(bool afterState)
    {
        RiverMap? riverMask = TerrainManager.RiverMap;
        byte[]? riverMaskData = riverMask?.GetRawData();
        if (riverMask == null || riverMaskData == null || changedChunks.Count == 0)
            return;

        int dataWidth = GetDataWidth();
        foreach (RiverMaskChunkDelta delta in changedChunks)
        {
            byte[] stateData = afterState ? delta.After : delta.Before;
            for (int row = 0; row < delta.Region.Height; row++)
            {
                int srcOffset = row * delta.Region.Width;
                int dstOffset = (delta.Region.Y + row) * dataWidth + delta.Region.X;
                Array.Copy(stateData, srcOffset, riverMaskData, dstOffset, delta.Region.Width);
            }
        }

        TerrainManager.MarkRiverMaskDirty();
    }

    private byte[] CopyChunk(byte[] source, TerrainChunkRegion chunk)
    {
        byte[] result = new byte[chunk.Width * chunk.Height];
        int dataWidth = GetDataWidth();

        for (int row = 0; row < chunk.Height; row++)
        {
            int srcOffset = (chunk.Y + row) * dataWidth + chunk.X;
            int dstOffset = row * chunk.Width;
            Array.Copy(source, srcOffset, result, dstOffset, chunk.Width);
        }

        return result;
    }

    private readonly record struct RiverMaskChunkDelta(TerrainChunkRegion Region, byte[] Before, byte[] After);
}
