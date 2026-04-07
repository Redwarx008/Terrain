#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Command for material painting operations.
/// Stores before/after states for the affected region only (Copy-on-Write).
/// </summary>
public sealed class PaintEditCommand : TerrainEditCommand
{
    private readonly string toolName;

    // State storage - RGBA data for the affected region
    private byte[]? beforeData;
    private byte[]? afterData;

    private const int BytesPerPixel = 4; // RGBA

    public override TerrainDataChannel AffectedChannel => TerrainDataChannel.MaterialIndex;
    public override string Description => $"{toolName} Material";

    public override long EstimatedSizeBytes =>
        (beforeData?.Length ?? 0) + (afterData?.Length ?? 0);

    public PaintEditCommand(TerrainManager terrainManager, string toolName)
        : base(terrainManager)
    {
        this.toolName = toolName;
    }

    protected override int GetDataWidth()
    {
        return TerrainManager.MaterialIndices?.Width ?? 0;
    }

    protected override int GetDataHeight()
    {
        return TerrainManager.MaterialIndices?.Height ?? 0;
    }

    /// <summary>
    /// Captures the before state. Called in BeginStroke.
    /// </summary>
    public override void CaptureBeforeState()
    {
        var indexMap = TerrainManager.MaterialIndices;
        if (indexMap == null) return;

        if (AffectedRegion.Width == 0 || AffectedRegion.Height == 0)
        {
            AffectedRegion = new Rectangle(0, 0, GetDataWidth(), GetDataHeight());
        }

        beforeData = CopyRegion(indexMap, AffectedRegion);
    }

    /// <summary>
    /// Captures the after state. Called in EndStroke.
    /// </summary>
    public override void CaptureAfterState()
    {
        var indexMap = TerrainManager.MaterialIndices;
        if (indexMap == null) return;

        afterData = CopyRegion(indexMap, AffectedRegion);
    }

    private byte[] CopyRegion(MaterialIndexMap source, Rectangle region)
    {
        var result = new byte[region.Width * region.Height * BytesPerPixel];
        int index = 0;

        for (int z = 0; z < region.Height; z++)
        {
            for (int x = 0; x < region.Width; x++)
            {
                var pixel = source.GetPixel(region.X + x, region.Y + z);
                result[index++] = pixel.Index;
                result[index++] = pixel.Weight;
                result[index++] = pixel.Projection;
                result[index++] = pixel.Rotation;
            }
        }

        return result;
    }

    public override void Execute()
    {
        ApplyState(afterData);
    }

    public override void Undo()
    {
        ApplyState(beforeData);
    }

    private void ApplyState(byte[]? stateData)
    {
        if (stateData == null) return;

        var indexMap = TerrainManager.MaterialIndices;
        if (indexMap == null) return;

        int index = 0;
        for (int z = 0; z < AffectedRegion.Height; z++)
        {
            for (int x = 0; x < AffectedRegion.Width; x++)
            {
                var pixel = new MaterialPixel
                {
                    Index = stateData[index++],
                    Weight = stateData[index++],
                    Projection = stateData[index++],
                    Rotation = stateData[index++]
                };
                indexMap.SetPixel(AffectedRegion.X + x, AffectedRegion.Y + z, pixel);
            }
        }

        // Mark the region as dirty for GPU sync
        TerrainManager.MarkDataDirty(TerrainDataChannel.MaterialIndex);
    }
}
