#nullable enable

using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Terrain.Editor.Services.Resources;

public sealed class HeightmapWriter
{
    public void Write(EditorResourceSession session, ushort[] heightData, int width, int height)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (heightData == null)
            throw new ArgumentNullException(nameof(heightData));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (heightData.Length < width * height)
            throw new ArgumentException("Height data is smaller than the requested image dimensions.", nameof(heightData));
        if (!session.Heightmap.IsWritable)
            throw new InvalidOperationException($"Heightmap target is read-only: {session.Heightmap.ResolvedPath}");

        Write(session.Heightmap.ResolvedPath, heightData, width, height);
    }

    internal void Write(string outputPath, ushort[] heightData, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be null or empty.", nameof(outputPath));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = new Image<L16>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<L16> row = accessor.GetRowSpan(y);
                int offset = y * width;
                for (int x = 0; x < row.Length; x++)
                    row[x] = new L16(heightData[offset + x]);
            }
        });
        image.SaveAsPng(outputPath);
    }
}
