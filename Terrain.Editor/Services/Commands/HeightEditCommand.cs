#nullable enable

using System;
using System.Collections.Generic;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Command for height editing operations (Raise, Lower, Smooth, Flatten).
/// Stores only changed chunks touched by one stroke.
/// </summary>
public sealed class HeightEditCommand : TerrainEditCommand
{
    private readonly string toolName;
    private readonly Dictionary<long, ushort[]> beforeChunkData = new();
    private readonly List<HeightChunkDelta> changedChunks = new();
    private long estimatedSizeBytes;

    public override TerrainDataChannel AffectedChannel => TerrainDataChannel.Height;
    public override string Description => $"{toolName} Terrain";
    public override long EstimatedSizeBytes => estimatedSizeBytes;

    public HeightEditCommand(TerrainManager terrainManager, string toolName)
        : base(terrainManager)
    {
        this.toolName = toolName;
    }

    protected override int GetDataWidth() => TerrainManager.HeightCacheWidth;
    protected override int GetDataHeight() => TerrainManager.HeightCacheHeight;

    protected override void CaptureBeforeChunk(TerrainChunkRegion chunk)
    {
        var heightData = TerrainManager.HeightDataCache;
        if (heightData == null || chunk.Width <= 0 || chunk.Height <= 0)
            return;

        beforeChunkData[chunk.Key] = CopyChunk(heightData, chunk);
    }

    protected override bool CaptureAfterStateAndFilter(IReadOnlyList<TerrainChunkRegion> chunks)
    {
        var heightData = TerrainManager.HeightDataCache;
        if (heightData == null)
            return false;

        changedChunks.Clear();
        estimatedSizeBytes = 0;

        foreach (var chunk in chunks)
        {
            if (!beforeChunkData.TryGetValue(chunk.Key, out var before))
                continue;

            var after = CopyChunk(heightData, chunk);
            if (before.AsSpan().SequenceEqual(after))
                continue;

            changedChunks.Add(new HeightChunkDelta(chunk, before, after));
            estimatedSizeBytes += (before.Length + after.Length) * sizeof(ushort);
        }

        beforeChunkData.Clear();
        return changedChunks.Count > 0;
    }

    private ushort[] CopyChunk(ushort[] source, TerrainChunkRegion chunk)
    {
        var result = new ushort[chunk.Width * chunk.Height];
        int dataWidth = GetDataWidth();

        for (int row = 0; row < chunk.Height; row++)
        {
            int srcOffset = (chunk.Y + row) * dataWidth + chunk.X;
            int dstOffset = row * chunk.Width;
            Array.Copy(source, srcOffset, result, dstOffset, chunk.Width);
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
        var heightData = TerrainManager.HeightDataCache;
        if (heightData == null || changedChunks.Count == 0)
            return;

        int dataWidth = GetDataWidth();

        foreach (var delta in changedChunks)
        {
            // Replay chunk rows with contiguous array copies to avoid per-pixel overhead.
            var stateData = afterState ? delta.After : delta.Before;
            for (int row = 0; row < delta.Region.Height; row++)
            {
                int srcOffset = row * delta.Region.Width;
                int dstOffset = (delta.Region.Y + row) * dataWidth + delta.Region.X;
                Array.Copy(stateData, srcOffset, heightData, dstOffset, delta.Region.Width);
            }

            float centerX = delta.Region.X + delta.Region.Width * 0.5f;
            float centerZ = delta.Region.Y + delta.Region.Height * 0.5f;
            float radius = MathF.Max(delta.Region.Width, delta.Region.Height) * 0.5f;
            TerrainManager.MarkDataDirty(TerrainDataChannel.Height, (int)centerX, (int)centerZ, radius);
        }
    }

    private readonly record struct HeightChunkDelta(TerrainChunkRegion Region, ushort[] Before, ushort[] After);
}
