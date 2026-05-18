#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Stores the user-authored river mask at half heightmap resolution.
/// Each texel is a raw Vic3-compatible river authoring value.
/// </summary>
public sealed class RiverMask
{
    private readonly byte[] data;

    public int Width { get; }
    public int Height { get; }

    public RiverMask(int width, int height, byte defaultValue = 0)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        data = new byte[width * height];
        Array.Fill(data, defaultValue);
    }

    public byte GetValue(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return 0;

        return data[y * Width + x];
    }

    public void SetValue(int x, int y, byte value)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;

        data[y * Width + x] = value;
    }

    public byte[] GetRawData() => data;

    public void Clear(byte value = 0)
    {
        Array.Fill(data, value);
    }
}
