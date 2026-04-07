#nullable enable

using System;
using System.Collections.Generic;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Base class for terrain editing commands with chunk-based state capture.
/// </summary>
public abstract class TerrainEditCommand : ICommand
{
    protected readonly TerrainManager TerrainManager;
    private readonly StrokeChunkTracker chunkTracker = new();

    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    protected TerrainEditCommand(TerrainManager terrainManager)
    {
        TerrainManager = terrainManager ?? throw new ArgumentNullException(nameof(terrainManager));
    }

    /// <summary>
    /// Marks touched chunks for the current stroke.
    /// The first time a chunk is touched, we capture its "before" state immediately
    /// so we never need to snapshot the whole map at stroke begin.
    /// </summary>
    public void MarkAffectedArea(int x, int z, float radius)
    {
        chunkTracker.MarkCircle(x, z, radius, GetDataWidth(), GetDataHeight(), CaptureBeforeChunk);
    }

    protected abstract int GetDataWidth();
    protected abstract int GetDataHeight();

    /// <summary>
    /// Called when a chunk is first touched by the active stroke.
    /// </summary>
    protected abstract void CaptureBeforeChunk(TerrainChunkRegion chunk);

    /// <summary>
    /// Captures after-state and filters unchanged chunks.
    /// Returns true when at least one chunk changed and the command should be committed.
    /// </summary>
    protected abstract bool CaptureAfterStateAndFilter(IReadOnlyList<TerrainChunkRegion> chunks);

    public bool PrepareForCommit()
    {
        var chunks = chunkTracker.GetRegions(GetDataWidth(), GetDataHeight());
        if (chunks.Count == 0)
            return false;

        return CaptureAfterStateAndFilter(chunks);
    }

    public abstract string Description { get; }
    public abstract TerrainDataChannel AffectedChannel { get; }
    public abstract long EstimatedSizeBytes { get; }
    public abstract void Execute();
    public abstract void Undo();
}
