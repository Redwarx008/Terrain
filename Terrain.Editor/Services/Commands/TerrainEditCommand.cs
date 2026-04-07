#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Base class for terrain editing commands with region-based state capture.
/// Implements Copy-on-Write optimization by only storing affected regions.
/// </summary>
public abstract class TerrainEditCommand : ICommand
{
    protected readonly TerrainManager TerrainManager;

    /// <summary>
    /// The bounding box of the affected region in pixel coordinates.
    /// </summary>
    protected Rectangle AffectedRegion;

    /// <summary>
    /// Timestamp when the command was created.
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    protected TerrainEditCommand(TerrainManager terrainManager)
    {
        TerrainManager = terrainManager ?? throw new ArgumentNullException(nameof(terrainManager));
    }

    /// <summary>
    /// Expands the affected region to include a new point.
    /// Called during ApplyStroke to accumulate the modified area.
    /// </summary>
    public void ExpandRegion(int x, int z, float radius)
    {
        int minX = Math.Max(0, (int)MathF.Floor(x - radius));
        int minZ = Math.Max(0, (int)MathF.Floor(z - radius));
        int maxX = Math.Min(GetDataWidth() - 1, (int)MathF.Ceiling(x + radius));
        int maxZ = Math.Min(GetDataHeight() - 1, (int)MathF.Ceiling(z + radius));

        if (AffectedRegion.Width == 0 && AffectedRegion.Height == 0)
        {
            AffectedRegion = new Rectangle(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }
        else
        {
            int newMinX = Math.Min(AffectedRegion.X, minX);
            int newMinZ = Math.Min(AffectedRegion.Y, minZ);
            int newMaxX = Math.Max(AffectedRegion.Right - 1, maxX);
            int newMaxZ = Math.Max(AffectedRegion.Bottom - 1, maxZ);
            AffectedRegion = new Rectangle(newMinX, newMinZ, newMaxX - newMinX + 1, newMaxZ - newMinZ + 1);
        }
    }

    /// <summary>
    /// Gets the width of the data being edited.
    /// </summary>
    protected abstract int GetDataWidth();

    /// <summary>
    /// Gets the height of the data being edited.
    /// </summary>
    protected abstract int GetDataHeight();

    /// <summary>
    /// Captures the before state. Called in BeginStroke.
    /// </summary>
    public abstract void CaptureBeforeState();

    /// <summary>
    /// Captures the after state. Called in EndStroke.
    /// </summary>
    public abstract void CaptureAfterState();

    public abstract string Description { get; }
    public abstract TerrainDataChannel AffectedChannel { get; }
    public abstract long EstimatedSizeBytes { get; }
    public abstract void Execute();
    public abstract void Undo();
}
