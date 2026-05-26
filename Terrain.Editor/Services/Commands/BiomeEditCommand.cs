#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Command for biome brush strokes. Stores only changed chunks touched by one stroke.
/// </summary>
public sealed class BiomeEditCommand : TerrainEditCommand
{
    private readonly byte biomeId;
    private readonly Dictionary<long, byte[]> beforeChunkData = new();
    private readonly List<BiomeChunkDelta> changedChunks = new();
    private long estimatedSizeBytes;

    public override TerrainDataChannel AffectedChannel => TerrainDataChannel.Biome;
    public override string Description => $"Biome Paint ({biomeId})";
    public override long EstimatedSizeBytes => estimatedSizeBytes;

    public BiomeEditCommand(TerrainManager terrainManager, byte biomeId)
        : base(terrainManager)
    {
        this.biomeId = biomeId;
    }

    protected override int GetDataWidth() => TerrainManager.BiomeMask?.Width ?? 0;
    protected override int GetDataHeight() => TerrainManager.BiomeMask?.Height ?? 0;

    protected override void CaptureBeforeChunk(TerrainChunkRegion chunk)
    {
        var mask = TerrainManager.BiomeMask;
        if (mask == null || chunk.Width <= 0 || chunk.Height <= 0)
            return;

        beforeChunkData[chunk.Key] = CopyChunk(mask.GetRawData(), chunk);
    }

    protected override bool CaptureAfterStateAndFilter(IReadOnlyList<TerrainChunkRegion> chunks)
    {
        var mask = TerrainManager.BiomeMask;
        if (mask == null)
            return false;

        var source = mask.GetRawData();
        changedChunks.Clear();
        estimatedSizeBytes = 0;

        foreach (var chunk in chunks)
        {
            if (!beforeChunkData.TryGetValue(chunk.Key, out var before))
                continue;

            var after = CopyChunk(source, chunk);
            if (before.AsSpan().SequenceEqual(after))
                continue;

            changedChunks.Add(new BiomeChunkDelta(chunk, before, after));
            estimatedSizeBytes += (before.Length + after.Length) * sizeof(byte);
        }

        beforeChunkData.Clear();
        return changedChunks.Count > 0;
    }

    private byte[] CopyChunk(byte[] source, TerrainChunkRegion chunk)
    {
        var result = new byte[chunk.Width * chunk.Height];
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
        var mask = TerrainManager.BiomeMask;
        if (mask == null || changedChunks.Count == 0)
            return;

        var dest = mask.GetRawData();
        int dataWidth = GetDataWidth();

        foreach (var delta in changedChunks)
        {
            var stateData = afterState ? delta.After : delta.Before;
            for (int row = 0; row < delta.Region.Height; row++)
            {
                int srcOffset = row * delta.Region.Width;
                int dstOffset = (delta.Region.Y + row) * dataWidth + delta.Region.X;
                Array.Copy(stateData, srcOffset, dest, dstOffset, delta.Region.Width);
            }

            float centerX = delta.Region.X + delta.Region.Width * 0.5f;
            float centerZ = delta.Region.Y + delta.Region.Height * 0.5f;
            float radius = MathF.Max(delta.Region.Width, delta.Region.Height) * 0.5f;
            TerrainManager.RegenerateMaterialIndices(centerX, centerZ, radius);
        }

        TerrainManager.MarkBiomeMaskDirty();
    }

    private readonly record struct BiomeChunkDelta(TerrainChunkRegion Region, byte[] Before, byte[] After);
}
