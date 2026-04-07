#nullable enable

using System;
using System.Collections.Generic;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Tracks stroke-touched chunks in a deterministic order.
/// </summary>
public sealed class StrokeChunkTracker
{
    public const int DefaultChunkSize = 64;

    private readonly int chunkSize;
    private readonly HashSet<long> chunkKeys = new();

    public int ChunkSize => chunkSize;
    public int Count => chunkKeys.Count;

    public StrokeChunkTracker(int chunkSize = DefaultChunkSize)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));

        this.chunkSize = chunkSize;
    }

    /// <summary>
    /// Marks all chunks intersecting the brush circle and invokes callback only for newly touched chunks.
    /// </summary>
    public void MarkCircle(int centerX, int centerZ, float radius, int dataWidth, int dataHeight, Action<TerrainChunkRegion> onChunkFirstTouched)
    {
        if (dataWidth <= 0 || dataHeight <= 0 || radius < 0f)
            return;

        int minX = Math.Max(0, (int)MathF.Floor(centerX - radius));
        int minZ = Math.Max(0, (int)MathF.Floor(centerZ - radius));
        int maxX = Math.Min(dataWidth - 1, (int)MathF.Ceiling(centerX + radius));
        int maxZ = Math.Min(dataHeight - 1, (int)MathF.Ceiling(centerZ + radius));

        if (minX > maxX || minZ > maxZ)
            return;

        int minChunkX = minX / chunkSize;
        int minChunkZ = minZ / chunkSize;
        int maxChunkX = maxX / chunkSize;
        int maxChunkZ = maxZ / chunkSize;

        for (int cz = minChunkZ; cz <= maxChunkZ; cz++)
        {
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                long key = ComposeKey(cx, cz);
                if (!chunkKeys.Add(key))
                    continue;

                onChunkFirstTouched(CreateRegion(key, dataWidth, dataHeight));
            }
        }
    }

    public List<TerrainChunkRegion> GetRegions(int dataWidth, int dataHeight)
    {
        var regions = new List<TerrainChunkRegion>(chunkKeys.Count);
        foreach (long key in chunkKeys)
        {
            regions.Add(CreateRegion(key, dataWidth, dataHeight));
        }

        // Keep stable replay order for easier debugging and deterministic tests.
        regions.Sort(static (a, b) =>
        {
            int byY = a.Y.CompareTo(b.Y);
            return byY != 0 ? byY : a.X.CompareTo(b.X);
        });
        return regions;
    }

    private TerrainChunkRegion CreateRegion(long key, int dataWidth, int dataHeight)
    {
        int cx = (int)(key & 0xFFFFFFFF);
        int cz = (int)(key >> 32);

        int x = cx * chunkSize;
        int y = cz * chunkSize;
        int width = Math.Max(0, Math.Min(chunkSize, dataWidth - x));
        int height = Math.Max(0, Math.Min(chunkSize, dataHeight - y));

        return new TerrainChunkRegion(key, x, y, width, height);
    }

    private static long ComposeKey(int chunkX, int chunkZ)
    {
        return ((long)chunkZ << 32) | (uint)chunkX;
    }
}

public readonly record struct TerrainChunkRegion(long Key, int X, int Y, int Width, int Height);
