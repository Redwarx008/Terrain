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

    private const int BytesPerPixel = MaterialIndexMap.BytesPerPixel;

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

        beforeChunkData[chunk.Key] = CopyChunk(indexMap.GetRawData(), indexMap.Width, chunk);
    }

    protected override bool CaptureAfterStateAndFilter(IReadOnlyList<TerrainChunkRegion> chunks)
    {
        var indexMap = TerrainManager.MaterialIndices;
        if (indexMap == null)
            return false;

        changedChunks.Clear();
        estimatedSizeBytes = 0;

        var rawData = indexMap.GetRawData();
        foreach (var chunk in chunks)
        {
            if (!beforeChunkData.TryGetValue(chunk.Key, out var before))
                continue;

            var after = CopyChunk(rawData, indexMap.Width, chunk);
            if (before.AsSpan().SequenceEqual(after))
                continue;

            changedChunks.Add(new PaintChunkDelta(chunk, before, after));
            estimatedSizeBytes += before.Length + after.Length;
        }

        beforeChunkData.Clear();
        return changedChunks.Count > 0;
    }

    private static byte[] CopyChunk(byte[] source, int dataWidth, TerrainChunkRegion chunk)
    {
        int rowBytes = chunk.Width * BytesPerPixel;
        var result = new byte[rowBytes * chunk.Height];

        for (int row = 0; row < chunk.Height; row++)
        {
            int srcOffset = ((chunk.Y + row) * dataWidth + chunk.X) * BytesPerPixel;
            int dstOffset = row * rowBytes;
            Array.Copy(source, srcOffset, result, dstOffset, rowBytes);
        }

        return result;
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

        var rawData = indexMap.GetRawData();
        int dataWidth = indexMap.Width;

        foreach (var delta in changedChunks)
        {
            // Replay chunk rows with raw RGBA block copies for speed.
            var stateData = afterState ? delta.After : delta.Before;
            int rowBytes = delta.Region.Width * BytesPerPixel;
            for (int row = 0; row < delta.Region.Height; row++)
            {
                int srcOffset = row * rowBytes;
                int dstOffset = ((delta.Region.Y + row) * dataWidth + delta.Region.X) * BytesPerPixel;
                Array.Copy(stateData, srcOffset, rawData, dstOffset, rowBytes);
            }
        }

        TerrainManager.MarkDataDirty(TerrainDataChannel.MaterialIndex);
    }

    private readonly record struct PaintChunkDelta(TerrainChunkRegion Region, byte[] Before, byte[] After);
}
