#nullable enable
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Stride.Core.Diagnostics;
using System;
using System.IO;

namespace Terrain.Editor.Services;

/// <summary>
/// Service for loading heightmap PNG files and creating Stride textures.
/// Supports 16-bit grayscale (L16) PNG format.
/// </summary>
public static class HeightmapLoader
{
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
            using var image = Image.Load<L16>(path);
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
            using var image = Image.Load<L16>(path);
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
            using var image = Image.Load<L16>(path);
            return (image.Width, image.Height);
        }
        catch
        {
            return null;
        }
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
