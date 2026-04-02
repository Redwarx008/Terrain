#nullable enable
using Stride.Core.Diagnostics;
using Stride.Graphics;
using System;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services;

/// <summary>
/// Creates one logical editor terrain backed by one or more heightmap slices.
/// </summary>
public static class TerrainSplitter
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");

    public static EditorTerrainEntity? CreateTerrainEntity(
        GraphicsDevice graphicsDevice,
        ushort[] heightData,
        int width,
        int height,
        IProgress<(int current, int total, string message)>? progress = null,
        int baseChunkSize = SplitTerrainConfig.DefaultBaseChunkSize)
    {
        if (graphicsDevice == null)
            throw new ArgumentNullException(nameof(graphicsDevice));
        if (heightData == null || heightData.Length == 0)
            throw new ArgumentNullException(nameof(heightData));

        var splitConfig = SplitTerrainConfig.Compute(width, height, baseChunkSize);
        progress?.Report((20, 100, $"Preparing {splitConfig.TotalSliceCount} height slice(s)..."));

        var entity = EditorTerrainEntity.CreateFromHeightmapData(
            graphicsDevice,
            heightData,
            width,
            height,
            splitConfig,
            baseChunkSize: baseChunkSize);

        if (entity != null)
        {
            Log.Info($"Created logical editor terrain: {width}x{height} using {splitConfig.TotalSliceCount} slice(s)");
        }

        return entity;
    }

    public static SplitTerrainConfig ComputeSplitConfig(string pngPath, int baseChunkSize = SplitTerrainConfig.DefaultBaseChunkSize)
    {
        var info = HeightmapLoader.LoadHeightmapInfo(pngPath);
        if (info == null)
        {
            throw new InvalidOperationException($"Failed to load heightmap info: {pngPath}");
        }

        return SplitTerrainConfig.Compute(info.Width, info.Height, baseChunkSize);
    }
}
