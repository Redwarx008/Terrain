#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Command for height editing operations (Raise, Lower, Smooth, Flatten).
/// Stores before/after states for the affected region only (Copy-on-Write).
/// </summary>
public sealed class HeightEditCommand : TerrainEditCommand
{
    private readonly string toolName;

    // State storage - only the affected region
    private ushort[]? beforeData;
    private ushort[]? afterData;

    public override TerrainDataChannel AffectedChannel => TerrainDataChannel.Height;
    public override string Description => $"{toolName} Terrain";

    public override long EstimatedSizeBytes =>
        ((beforeData?.Length ?? 0) + (afterData?.Length ?? 0)) * sizeof(ushort);

    public HeightEditCommand(TerrainManager terrainManager, string toolName)
        : base(terrainManager)
    {
        this.toolName = toolName;
    }

    protected override int GetDataWidth() => TerrainManager.HeightCacheWidth;
    protected override int GetDataHeight() => TerrainManager.HeightCacheHeight;

    /// <summary>
    /// Captures the before state. Called in BeginStroke.
    /// </summary>
    public override void CaptureBeforeState()
    {
        var heightData = TerrainManager.HeightDataCache;
        if (heightData == null) return;

        // Initialize region to empty if not yet set
        if (AffectedRegion.Width == 0 || AffectedRegion.Height == 0)
        {
            AffectedRegion = new Rectangle(0, 0, GetDataWidth(), GetDataHeight());
        }

        beforeData = CopyRegion(heightData, AffectedRegion);
    }

    /// <summary>
    /// Captures the after state. Called in EndStroke.
    /// </summary>
    public override void CaptureAfterState()
    {
        var heightData = TerrainManager.HeightDataCache;
        if (heightData == null) return;

        afterData = CopyRegion(heightData, AffectedRegion);
    }

    private ushort[] CopyRegion(ushort[] source, Rectangle region)
    {
        var result = new ushort[region.Width * region.Height];
        int dataWidth = GetDataWidth();

        for (int z = 0; z < region.Height; z++)
        {
            int srcOffset = (region.Y + z) * dataWidth + region.X;
            int dstOffset = z * region.Width;
            Array.Copy(source, srcOffset, result, dstOffset, region.Width);
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

    private void ApplyState(ushort[]? stateData)
    {
        if (stateData == null) return;

        var heightData = TerrainManager.HeightDataCache;
        if (heightData == null) return;

        int dataWidth = GetDataWidth();

        // Copy state back to the height data
        for (int z = 0; z < AffectedRegion.Height; z++)
        {
            int srcOffset = z * AffectedRegion.Width;
            int dstOffset = (AffectedRegion.Y + z) * dataWidth + AffectedRegion.X;
            Array.Copy(stateData, srcOffset, heightData, dstOffset, AffectedRegion.Width);
        }

        // Mark the region as dirty for GPU sync
        float radius = MathF.Max(AffectedRegion.Width, AffectedRegion.Height) * 0.5f;
        float centerX = AffectedRegion.X + AffectedRegion.Width * 0.5f;
        float centerZ = AffectedRegion.Y + AffectedRegion.Height * 0.5f;
        TerrainManager.MarkDataDirty(TerrainDataChannel.Height, (int)centerX, (int)centerZ, radius);
    }
}
