#nullable enable

using System;
using System.Collections.Generic;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Command for material painting operations.
/// Stores only changed chunks touched by one stroke.
/// </summary>
public sealed class PaintEditCommand : TerrainEditCommand
{
    private readonly string toolName;
    private readonly Dictionary<long, byte[]> beforeChunkData = new();
    private readonly List<PaintChunkDelta> changedChunks = new();
    private long estimatedSizeBytes;

    public override TerrainDataChannel AffectedChannel => TerrainDataChannel.MaterialIndex;
    public override string Description => $"{toolName} Material";
    public override long EstimatedSizeBytes => estimatedSizeBytes;

    public PaintEditCommand(TerrainManager terrainManager, string toolName)
        : base(terrainManager)
    {
        this.toolName = toolName;
    }

    protected override int GetDataWidth() => TerrainManager.MaterialIndices?.Width ?? 0;
    protected override int GetDataHeight() => TerrainManager.MaterialIndices?.Height ?? 0;

    protected override void CaptureBeforeChunk(TerrainChunkRegion chunk)
    {
        var indexMap = TerrainManager.MaterialIndices;
        if (indexMap == null || chunk.Width <= 0 || chunk.Height <= 0)
            return;

        beforeChunkData[chunk.Key] = indexMap.CopyRegionToBytes(chunk.X, chunk.Y, chunk.Width, chunk.Height);
    }

    protected override bool CaptureAfterStateAndFilter(IReadOnlyList<TerrainChunkRegion> chunks)
    {
        var indexMap = TerrainManager.MaterialIndices;
        if (indexMap == null)
            return false;

        changedChunks.Clear();
        estimatedSizeBytes = 0;

        foreach (var chunk in chunks)
        {
            if (!beforeChunkData.TryGetValue(chunk.Key, out var before))
                continue;

            var after = indexMap.CopyRegionToBytes(chunk.X, chunk.Y, chunk.Width, chunk.Height);
            if (before.AsSpan().SequenceEqual(after))
                continue;

            changedChunks.Add(new PaintChunkDelta(chunk, before, after));
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
        var indexMap = TerrainManager.MaterialIndices;
        if (indexMap == null || changedChunks.Count == 0)
            return;

        foreach (var delta in changedChunks)
        {
            var stateData = afterState ? delta.After : delta.Before;
            indexMap.SetRegionFromBytes(delta.Region.X, delta.Region.Y, delta.Region.Width, delta.Region.Height, stateData);
        }

        TerrainManager.MarkDataDirty(TerrainDataChannel.MaterialIndex);
    }

    private readonly record struct PaintChunkDelta(TerrainChunkRegion Region, byte[] Before, byte[] After);
}
