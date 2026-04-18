#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Stores the user-authored climate classification map.
/// Each texel contains a single climate id that later drives rule evaluation.
/// </summary>
public sealed class ClimateMask
{
    private readonly byte[] data;

    public int Width { get; }
    public int Height { get; }

    public ClimateMask(int width, int height, byte defaultClimateId = 0)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        data = new byte[width * height];
        Array.Fill(data, defaultClimateId);
    }

    public byte GetValue(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return 0;

        return data[y * Width + x];
    }

    public void SetValue(int x, int y, byte climateId)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;

        data[y * Width + x] = climateId;
    }

    public byte[] GetRawData() => data;

    public void Clear(byte climateId = 0)
    {
        Array.Fill(data, climateId);
    }
}
