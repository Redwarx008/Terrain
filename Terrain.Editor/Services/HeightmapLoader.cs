#nullable enable
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using System;
using System.IO;
using System.Threading.Tasks;

// Disambiguate Image type between Stride and ImageSharp
using HeightmapImage = SixLabors.ImageSharp.Image;

namespace Terrain.Editor.Services;

/// <summary>
/// Service for loading heightmap PNG files and creating Stride textures.
/// Supports 16-bit grayscale (L16) PNG format.
/// </summary>
public static class HeightmapLoader
{
    private const int DefaultBaseChunkSize = 32;
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");

    /// <summary>
    /// Loads a heightmap PNG file and returns its metadata.
    /// </summary>
    /// <param name="path">Path to the PNG file</param>
    /// <returns>Heightmap info if successful, null otherwise</returns>
    public static HeightmapInfo? LoadHeightmapInfo(string path)
    {
        try
        {
            using var image = HeightmapImage.Load<L16>(path);
            return new HeightmapInfo
            {
                Width = image.Width,
                Height = image.Height,
                Path = path
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load heightmap '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Validates that a file is a valid heightmap PNG.
    /// </summary>
    public static bool IsValidHeightmap(string path)
    {
        if (!File.Exists(path))
            return false;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".png")
            return false;

        try
        {
            using var image = HeightmapImage.Load<L16>(path);
            return image.Width > 1 && image.Height > 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the dimensions of a heightmap without fully loading it.
    /// </summary>
    public static (int Width, int Height)? GetDimensions(string path)
    {
        try
        {
            using var image = HeightmapImage.Load<L16>(path);
            return (image.Width, image.Height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads heightmap pixel data from PNG file.
    /// </summary>
    /// <param name="pngPath">Path to the PNG file</param>
    /// <param name="width">Output width</param>
    /// <param name="height">Output height</param>
    /// <returns>Height data as ushort array, or null on failure</returns>
    public static ushort[]? LoadHeightData(string pngPath, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var image = HeightmapImage.Load<L16>(pngPath);
            width = image.Width;
            height = image.Height;

            var heightData = new ushort[width * height];
            int localWidth = width;  // Capture for lambda

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    int rowOffset = y * localWidth;
                    for (int x = 0; x < pixelRow.Length; x++)
                    {
                        heightData[rowOffset + x] = pixelRow[x].PackedValue;
                    }
                }
            });

            Log.Info($"Loaded heightmap data: {width}x{height}");
            return heightData;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load heightmap data from '{pngPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generates MinMaxErrorMap hierarchy from height data.
    /// Per CONTEXT.md: Copy logic from TerrainPreProcessor/Models/MinMaxErrorMap.cs.
    /// </summary>
    public static EditorMinMaxErrorMap[] GenerateMinMaxErrorMaps(
        ushort[] heightData,
        int width,
        int height,
        int baseChunkSize)
    {
        if (heightData == null || heightData.Length == 0)
            throw new ArgumentNullException(nameof(heightData));

        if (width <= 1 || height <= 1)
            throw new ArgumentException("Invalid dimensions");

        // Convert to float array for processing
        float[] rawHeights = new float[width * height];
        for (int i = 0; i < heightData.Length; i++)
        {
            rawHeights[i] = (float)heightData[i];
        }

        // Calculate LOD levels
        int lodLevelCount = CalculateLodLevels(Math.Max(width, height), baseChunkSize);

        // Initialize base level
        int baseDimX = (int)Math.Ceiling(width / (float)baseChunkSize);
        int baseDimY = (int)Math.Ceiling(height / (float)baseChunkSize);

        var maps = new EditorMinMaxErrorMap[lodLevelCount];
        maps[0] = new EditorMinMaxErrorMap(baseDimX, baseDimY);

        // Initialize base level MinMaxError (parallel for performance)
        int chunkVertices = baseChunkSize + 1;
        int chunkX = baseDimX;
        int chunkY = baseDimY;
        int totalChunks = chunkX * chunkY;

        Parallel.For(0, totalChunks, index =>
        {
            int yChunkIndex = index / chunkX;
            int xChunkIndex = index % chunkX;

            int y = yChunkIndex * baseChunkSize;
            int x = xChunkIndex * baseChunkSize;
            int sizeX = Math.Min(chunkVertices, width - x);
            int sizeY = Math.Min(chunkVertices, height - y);

            GetAreaMinMaxHeight(rawHeights, width, height, x, y, sizeX, sizeY, out float minHeight, out float maxHeight);
            float error = CalculateGeometricError(rawHeights, width, height, x, y, sizeX, sizeY, 0);

            maps[0].Set(xChunkIndex, yChunkIndex, minHeight, maxHeight, error);
        });

        // Generate higher LOD levels
        for (int i = 1; i < lodLevelCount; i++)
        {
            maps[i] = CreateFromHigherDetail(rawHeights, width, height, maps[i - 1], i, baseChunkSize);
        }

        Log.Info($"Generated {lodLevelCount} MinMaxErrorMap levels");
        return maps;
    }

    private static EditorMinMaxErrorMap CreateFromHigherDetail(
        float[] rawHeights,
        int mapWidth,
        int mapHeight,
        EditorMinMaxErrorMap higherDetail,
        int lodLevel,
        int baseChunkSize)
    {
        int srcDimX = higherDetail.Width;
        int srcDimY = higherDetail.Height;

        int dimX = (srcDimX + 1) >> 1;
        int dimY = (srcDimY + 1) >> 1;

        var dst = new EditorMinMaxErrorMap(dimX, dimY);
        int totalDst = dimX * dimY;

        // Min/Max aggregation
        Parallel.For(0, totalDst, index =>
        {
            int dstY = index / dimX;
            int dstX = index % dimX;

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    int srcX = (dstX << 1) + dx;
                    int srcY = (dstY << 1) + dy;

                    if (srcX < srcDimX && srcY < srcDimY)
                    {
                        higherDetail.Get(srcX, srcY, out float childMin, out float childMax, out _);
                        min = MathF.Min(min, childMin);
                        max = MathF.Max(max, childMax);
                    }
                }
            }

            dst.Set(dstX, dstY, min, max, 0);
        });

        // Calculate geometric error
        int chunkSize = baseChunkSize << lodLevel;

        Parallel.For(0, totalDst, index =>
        {
            int y = index / dimX;
            int x = index % dimX;

            int startX = x * chunkSize;
            int startY = y * chunkSize;
            int sizeX = Math.Max(1, Math.Min(chunkSize + 1, mapWidth - startX));
            int sizeY = Math.Max(1, Math.Min(chunkSize + 1, mapHeight - startY));

            float error = CalculateGeometricError(rawHeights, mapWidth, mapHeight, startX, startY, sizeX, sizeY, lodLevel);
            dst.Get(x, y, out float min, out float max, out _);
            dst.Set(x, y, min, max, error);
        });

        return dst;
    }

    private static void GetAreaMinMaxHeight(
        float[] rawHeights,
        int mapW,
        int mapH,
        int startX,
        int startY,
        int sizeX,
        int sizeY,
        out float minHeight,
        out float maxHeight)
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        int endX = startX + sizeX - 1;
        int endY = startY + sizeY - 1;

        for (int y = startY; y <= endY; y++)
        {
            int clampedY = Math.Clamp(y, 0, mapH - 1);
            int rowBase = clampedY * mapW;
            for (int x = startX; x <= endX; x++)
            {
                int clampedX = Math.Clamp(x, 0, mapW - 1);
                float height = rawHeights[clampedX + rowBase];
                if (height < min) min = height;
                if (height > max) max = height;
            }
        }

        if (min == float.MaxValue) min = 0f;
        if (max == float.MinValue) max = 0f;

        minHeight = min;
        maxHeight = max;
    }

    private static float CalculateGeometricError(
        float[] rawHeights,
        int mapW,
        int mapH,
        int startX,
        int startY,
        int sizeX,
        int sizeY,
        int lod)
    {
        float maxError = 0f;
        int stride = 1 << lod;
        int halfStride = stride / 2;

        float GetHeight(int x, int y)
        {
            int cx = Math.Clamp(x, 0, mapW - 1);
            int cy = Math.Clamp(y, 0, mapH - 1);
            return rawHeights[cx + cy * mapW];
        }

        // Horizontal direction error
        for (int y = startY; y < startY + sizeY; y += stride)
        {
            for (int x = startX + halfStride; x < startX + sizeX - halfStride; x += stride)
            {
                float height = GetHeight(x, y);
                float left = GetHeight(x - halfStride, y);
                float right = GetHeight(x + halfStride, y);
                float simplifiedHeight = (left + right) / 2;
                float error = MathF.Abs(simplifiedHeight - height);
                maxError = MathF.Max(maxError, error);
            }
        }

        // Vertical direction error
        for (int y = startY + halfStride; y < startY + sizeY - halfStride; y += stride)
        {
            for (int x = startX; x < startX + sizeX; x += stride)
            {
                float height = GetHeight(x, y);
                float up = GetHeight(x, y + halfStride);
                float down = GetHeight(x, y - halfStride);
                float simplifiedHeight = (up + down) / 2;
                float error = MathF.Abs(simplifiedHeight - height);
                maxError = MathF.Max(maxError, error);
            }
        }

        // Diagonal direction error
        for (int y = startY + halfStride; y < startY + sizeY - halfStride; y += stride)
        {
            for (int x = startX + halfStride; x < startX + sizeX - halfStride; x += stride)
            {
                float height = GetHeight(x, y);
                float upLeft = GetHeight(x - halfStride, y + halfStride);
                float downRight = GetHeight(x + halfStride, y - halfStride);
                float simplifiedHeight = (upLeft + downRight) / 2;
                float error = MathF.Abs(simplifiedHeight - height);
                maxError = MathF.Max(maxError, error);
            }
        }

        return maxError;
    }

    /// <summary>
    /// Creates GPU texture from height data.
    /// </summary>
    public static Texture CreateHeightmapTexture(
        GraphicsDevice graphicsDevice,
        ushort[] heightData,
        int width,
        int height)
    {
        if (graphicsDevice == null)
            throw new ArgumentNullException(nameof(graphicsDevice));

        if (heightData == null || heightData.Length == 0)
            throw new ArgumentNullException(nameof(heightData));

        // Create texture with initial data (Stride will handle upload)
        var texture = Texture.New2D(
            graphicsDevice,
            width,
            height,
            PixelFormat.R16_UNorm,
            heightData);

        Log.Info($"Created heightmap texture: {width}x{height}");
        return texture;
    }

    /// <summary>
    /// Calculates LOD level count from heightmap dimensions.
    /// </summary>
    public static int CalculateLodLevels(int maxDimension, int baseChunkSize)
    {
        int levels = 0;
        int current = maxDimension - 1;  // Vertex count = size - 1

        while (current > baseChunkSize)
        {
            current = (current + 1) / 2;
            levels++;
        }

        return levels + 1;  // +1 includes finest level
    }
}

/// <summary>
/// Information about a loaded heightmap.
/// </summary>
public sealed class HeightmapInfo
{
    public required string Path { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}

/// <summary>
/// Editor-specific MinMaxErrorMap (simpler than TerrainPreProcessor version).
/// Stores min/max/error per chunk for LOD selection.
/// </summary>
public sealed class EditorMinMaxErrorMap
{
    private readonly float[] data;

    public int Width { get; }
    public int Height { get; }

    public EditorMinMaxErrorMap(int width, int height)
    {
        Width = width;
        Height = height;
        data = new float[width * height * 3];
    }

    public void Set(int x, int y, float min, float max, float error)
    {
        int index = (x + y * Width) * 3;
        data[index] = min;
        data[index + 1] = max;
        data[index + 2] = error;
    }

    public void Get(int x, int y, out float min, out float max, out float error)
    {
        int index = (x + y * Width) * 3;
        min = data[index];
        max = data[index + 1];
        error = data[index + 2];
    }

    public void GetSubNodesExist(int parentX, int parentY, out bool tl, out bool tr, out bool bl, out bool br)
    {
        int x = parentX * 2;
        int y = parentY * 2;
        tl = x < Width && y < Height;
        tr = x + 1 < Width && y < Height;
        bl = x < Width && y + 1 < Height;
        br = x + 1 < Width && y + 1 < Height;
    }

    public void GetGlobalMinMax(out float minHeight, out float maxHeight)
    {
        minHeight = float.MaxValue;
        maxHeight = float.MinValue;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Get(x, y, out var min, out var max, out _);
                minHeight = MathF.Min(minHeight, min);
                maxHeight = MathF.Max(maxHeight, max);
            }
        }

        if (minHeight == float.MaxValue)
        {
            minHeight = 0.0f;
            maxHeight = 0.0f;
        }
    }
}
