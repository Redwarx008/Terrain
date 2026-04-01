#nullable enable
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using System;
using System.Collections.Generic;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services;

/// <summary>
/// Splits large heightmaps into manageable chunks.
/// Per CONTEXT.md: Automatic grid splitting for heightmaps > 16k.
/// </summary>
public static class TerrainSplitter
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");

    /// <summary>
    /// Splits a large heightmap into multiple EditorTerrainEntity instances.
    /// </summary>
    /// <param name="graphicsDevice">Graphics device for GPU resource creation</param>
    /// <param name="pngPath">Path to the source heightmap PNG</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>List of EditorTerrainEntity instances (may be 1 if no split needed)</returns>
    public static List<EditorTerrainEntity> SplitAndCreateEntities(
        GraphicsDevice graphicsDevice,
        string pngPath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        if (graphicsDevice == null)
            throw new ArgumentNullException(nameof(graphicsDevice));

        if (string.IsNullOrEmpty(pngPath))
            throw new ArgumentNullException(nameof(pngPath));

        // Load source heightmap
        using var image = SixLabors.ImageSharp.Image.Load<L16>(pngPath);
        int sourceWidth = image.Width;
        int sourceHeight = image.Height;

        // Check if splitting is needed
        if (!SplitTerrainConfig.RequiresSplit(sourceWidth, sourceHeight))
        {
            // No split needed - create single entity
            progress?.Report((0, 100, "Loading single terrain..."));
            var singleEntity = EditorTerrainEntity.CreateFromHeightmap(graphicsDevice, pngPath);
            return singleEntity != null
                ? new List<EditorTerrainEntity> { singleEntity }
                : new List<EditorTerrainEntity>();
        }

        // Compute split configuration
        var config = SplitTerrainConfig.Compute(sourceWidth, sourceHeight);
        progress?.Report((0, config.TotalChunkCount, $"Splitting into {config.TotalChunkCount} chunks..."));

        var entities = new List<EditorTerrainEntity>();

        for (int chunkZ = 0; chunkZ < config.ChunkCountZ; chunkZ++)
        {
            for (int chunkX = 0; chunkX < config.ChunkCountX; chunkX++)
            {
                int chunkIndex = chunkZ * config.ChunkCountX + chunkX;
                progress?.Report((chunkIndex, config.TotalChunkCount, $"Creating chunk ({chunkX}, {chunkZ})..."));

                var (startX, startZ, width, height, worldOffset) = config.GetChunkBounds(chunkX, chunkZ);

                // Extract chunk data from source image
                var chunkData = ExtractChunkData(image, startX, startZ, width, height);

                // Create entity from chunk data
                var entity = CreateEntityFromChunkData(
                    graphicsDevice,
                    chunkData,
                    width,
                    height,
                    chunkX,
                    chunkZ,
                    worldOffset);

                if (entity != null)
                {
                    entities.Add(entity);
                }
            }
        }

        Log.Info($"Created {entities.Count} terrain chunks from {sourceWidth}x{sourceHeight} heightmap");
        return entities;
    }

    /// <summary>
    /// Extracts height data for a specific chunk region.
    /// </summary>
    private static ushort[] ExtractChunkData(SixLabors.ImageSharp.Image<L16> source, int startX, int startZ, int width, int height)
    {
        var data = new ushort[width * height];

        source.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                int srcY = Math.Clamp(startZ + y, 0, accessor.Height - 1);
                var srcRow = accessor.GetRowSpan(srcY);

                for (int x = 0; x < width; x++)
                {
                    int srcX = Math.Clamp(startX + x, 0, srcRow.Length - 1);
                    data[y * width + x] = srcRow[srcX].PackedValue;
                }
            }
        });

        return data;
    }

    /// <summary>
    /// Creates an EditorTerrainEntity from extracted chunk data.
    /// </summary>
    private static EditorTerrainEntity? CreateEntityFromChunkData(
        GraphicsDevice graphicsDevice,
        ushort[] chunkData,
        int width,
        int height,
        int chunkX,
        int chunkZ,
        Vector3 worldOffset)
    {
        try
        {
            // Use the new CreateFromHeightmapData method that accepts in-memory data
            var entity = EditorTerrainEntity.CreateFromHeightmapData(
                graphicsDevice,
                chunkData,
                width,
                height,
                chunkX,
                chunkZ,
                worldOffset);

            return entity;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create terrain entity for chunk ({chunkX}, {chunkZ}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Computes the split configuration for a heightmap file.
    /// </summary>
    public static SplitTerrainConfig ComputeSplitConfig(string pngPath)
    {
        var info = HeightmapLoader.LoadHeightmapInfo(pngPath);
        if (info == null)
        {
            throw new InvalidOperationException($"Failed to load heightmap info: {pngPath}");
        }

        return SplitTerrainConfig.Compute(info.Width, info.Height);
    }
}
