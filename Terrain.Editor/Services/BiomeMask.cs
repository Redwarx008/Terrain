#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Stores the user-authored biome classification map.
/// Each texel contains a single biome id that later drives rule evaluation.
/// </summary>
public sealed class BiomeMask
{
    private readonly byte[] data;

    public int Width { get; }
    public int Height { get; }

    public BiomeMask(int width, int height, byte defaultBiomeId = 0)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        data = new byte[width * height];
        Array.Fill(data, defaultBiomeId);
    }

    public byte GetValue(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return 0;

        return data[y * Width + x];
    }

    public void SetValue(int x, int y, byte biomeId)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;

        data[y * Width + x] = biomeId;
    }

    public byte[] GetRawData() => data;

    public void Clear(byte biomeId = 0)
    {
        Array.Fill(data, biomeId);
    }
}
