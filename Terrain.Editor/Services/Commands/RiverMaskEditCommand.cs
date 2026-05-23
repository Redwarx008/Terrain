#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

public sealed class RiverMaskEditCommand : TerrainEditCommand
{
    private readonly Dictionary<long, RiverCell[]> beforeChunkData = new();
    private readonly List<RiverMaskChunkDelta> changedChunks = new();
    private long estimatedSizeBytes;

    public override TerrainDataChannel AffectedChannel => TerrainDataChannel.RiverMask;
    public override string Description => "Paint River";
    public override long EstimatedSizeBytes => estimatedSizeBytes;

    public RiverMaskEditCommand(TerrainManager terrainManager)
        : base(terrainManager)
    {
    }

    private RiverCell[,]? RiverMap => TerrainManager.RiverMap;

    protected override int GetDataWidth() => RiverMap?.GetLength(0) ?? 0;
    protected override int GetDataHeight() => RiverMap?.GetLength(1) ?? 0;

    protected override void CaptureBeforeChunk(TerrainChunkRegion chunk)
    {
        RiverCell[,]? map = RiverMap;
        if (map == null || chunk.Width <= 0 || chunk.Height <= 0)
            return;

        beforeChunkData[chunk.Key] = CopyChunk(map, chunk);
    }

    protected override bool CaptureAfterStateAndFilter(IReadOnlyList<TerrainChunkRegion> chunks)
    {
        RiverCell[,]? map = RiverMap;
        if (map == null)
            return false;

        changedChunks.Clear();
        estimatedSizeBytes = 0;

        foreach (TerrainChunkRegion chunk in chunks)
        {
            if (!beforeChunkData.TryGetValue(chunk.Key, out RiverCell[]? before))
                continue;

            RiverCell[] after = CopyChunk(map, chunk);
            if (before.AsSpan().SequenceEqual(after))
                continue;

            changedChunks.Add(new RiverMaskChunkDelta(chunk, before, after));
            estimatedSizeBytes += before.Length * 2 + after.Length * 2;
        }

        beforeChunkData.Clear();
        return changedChunks.Count > 0;
    }

    public override void Execute() => ApplyState(afterState: true);

    public override void Undo() => ApplyState(afterState: false);

    private void ApplyState(bool afterState)
    {
        RiverCell[,]? map = RiverMap;
        if (map == null || changedChunks.Count == 0)
            return;

        int w = GetDataWidth();
        foreach (var delta in changedChunks)
        {
            RiverCell[] stateData = afterState ? delta.After : delta.Before;
            var chunk = delta.Region;
            for (int row = 0; row < chunk.Height; row++)
            {
                int srcOffset = row * chunk.Width;
                for (int col = 0; col < chunk.Width; col++)
                    map[chunk.X + col, chunk.Y + row] = stateData[srcOffset + col];
            }
        }

        TerrainManager.MarkRiverMaskDirty();
    }

    private static RiverCell[] CopyChunk(RiverCell[,] source, TerrainChunkRegion chunk)
    {
        var result = new RiverCell[chunk.Width * chunk.Height];
        int w = source.GetLength(0);
        for (int row = 0; row < chunk.Height; row++)
            for (int col = 0; col < chunk.Width; col++)
                result[row * chunk.Width + col] = source[chunk.X + col, chunk.Y + row];
        return result;
    }

    private readonly record struct RiverMaskChunkDelta(TerrainChunkRegion Region, RiverCell[] Before, RiverCell[] After);
}
