#nullable enable

using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace Terrain.Resources;

public readonly record struct RuntimeBiomeMaskData(byte[] Data, int Width, int Height);

public static class RuntimeBiomeMaskReader
{
    public static RuntimeBiomeMaskData ReadFrom(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Biome mask file was not found.", filePath);

        using Image<L8> image = ImageSharpImage.Load<L8>(filePath);
        if (image.Width <= 0 || image.Height <= 0)
            throw new InvalidDataException($"Biome mask image has invalid dimensions: {filePath}");

        var data = new byte[image.Width * image.Height];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                data[y * image.Width + x] = image[x, y].PackedValue;
            }
        }

        return new RuntimeBiomeMaskData(data, image.Width, image.Height);
    }
}
