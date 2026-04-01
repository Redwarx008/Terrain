#nullable enable
using Stride.Core.Mathematics;
using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Configuration for split terrain chunks.
/// Per CONTEXT.md: Split heightmaps > 16k into grid with 1 sample overlap.
/// </summary>
public sealed class SplitTerrainConfig
{
    /// <summary>
    /// Maximum chunk size before splitting is required (16k x 16k).
    /// </summary>
    public const int MaxChunkSize = 16384;

    /// <summary>
    /// Source heightmap dimensions.
    /// </summary>
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }

    /// <summary>
    /// Number of chunks in X and Z directions.
    /// </summary>
    public int ChunkCountX { get; init; }
    public int ChunkCountZ { get; init; }

    /// <summary>
    /// Size of each chunk in samples (may vary for edge chunks).
    /// </summary>
    public int ChunkSize { get; init; }

    /// <summary>
    /// Total number of chunks.
    /// </summary>
    public int TotalChunkCount => ChunkCountX * ChunkCountZ;

    /// <summary>
    /// Computes the split configuration for a given heightmap size.
    /// </summary>
    public static SplitTerrainConfig Compute(int sourceWidth, int sourceHeight)
    {
        int chunkCountX = (sourceWidth + MaxChunkSize - 1) / MaxChunkSize;
        int chunkCountZ = (sourceHeight + MaxChunkSize - 1) / MaxChunkSize;

        // At least 1 chunk in each dimension
        chunkCountX = Math.Max(1, chunkCountX);
        chunkCountZ = Math.Max(1, chunkCountZ);

        // Calculate actual chunk size (distribute evenly)
        int chunkSize = Math.Max(
            (sourceWidth + chunkCountX - 1) / chunkCountX,
            (sourceHeight + chunkCountZ - 1) / chunkCountZ);

        // Cap at MaxChunkSize
        chunkSize = Math.Min(chunkSize, MaxChunkSize);

        return new SplitTerrainConfig
        {
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            ChunkCountX = chunkCountX,
            ChunkCountZ = chunkCountZ,
            ChunkSize = chunkSize
        };
    }

    /// <summary>
    /// Gets the sample range for a specific chunk.
    /// Per CONTEXT.md: 1 sample overlap on shared edges.
    /// </summary>
    public (int StartX, int StartZ, int Width, int Height, Vector3 WorldOffset) GetChunkBounds(int chunkX, int chunkZ)
    {
        // Calculate start position with overlap
        // First chunk starts at 0, subsequent chunks start 1 sample earlier to overlap
        int startX = chunkX == 0 ? 0 : chunkX * ChunkSize - 1;
        int startZ = chunkZ == 0 ? 0 : chunkZ * ChunkSize - 1;

        // Calculate dimensions (accounting for overlap and source bounds)
        int endX = Math.Min((chunkX + 1) * ChunkSize, SourceWidth);
        int endZ = Math.Min((chunkZ + 1) * ChunkSize, SourceHeight);

        int width = endX - startX;
        int height = endZ - startZ;

        // World offset: chunk position in world space
        // Non-first chunks offset by (chunkIndex * ChunkSize - 1) to account for overlap
        float worldOffsetX = chunkX == 0 ? 0 : chunkX * ChunkSize - 1;
        float worldOffsetZ = chunkZ == 0 ? 0 : chunkZ * ChunkSize - 1;

        return (startX, startZ, width, height, new Vector3(worldOffsetX, 0, worldOffsetZ));
    }

    /// <summary>
    /// Checks if splitting is required for the given dimensions.
    /// </summary>
    public static bool RequiresSplit(int width, int height)
    {
        return width > MaxChunkSize || height > MaxChunkSize;
    }
}
