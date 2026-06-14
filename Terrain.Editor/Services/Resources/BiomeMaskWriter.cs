#nullable enable

using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Terrain.Editor.Services.Resources;

public sealed class BiomeMaskWriter
{
    public void Write(EditorResourceSession session, BiomeMask mask)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (mask == null)
            throw new ArgumentNullException(nameof(mask));
        if (!session.BiomeMask.IsWritable)
            throw new InvalidOperationException($"Biome mask target is read-only: {session.BiomeMask.ResolvedPath}");

        Write(session.BiomeMask.ResolvedPath, mask);
    }

    internal void Write(string outputPath, BiomeMask mask)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be null or empty.", nameof(outputPath));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = new Image<L8>(mask.Width, mask.Height);
        image.ProcessPixelRows(accessor =>
        {
            byte[] source = mask.GetRawData();
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<L8> row = accessor.GetRowSpan(y);
                int offset = y * mask.Width;
                for (int x = 0; x < row.Length; x++)
                    row[x] = new L8(source[offset + x]);
            }
        });
        image.SaveAsPng(outputPath);
    }
}
