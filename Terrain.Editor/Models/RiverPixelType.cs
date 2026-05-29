#nullable enable

using System;
using SixLabors.ImageSharp.PixelFormats;

namespace Terrain.Editor.Models;

public enum RiverPixelType : byte
{
    Land = 0,
    River = 1,
    Source = 2,
    Confluence = 3,
    Bifurcation = 4,
    Ocean = 5,
}

public readonly record struct RiverCell(RiverPixelType Type, byte Width = 0)
{
    private static readonly (Rgba32 Color, float HalfWidth)[] WidthPalette =
    [
        (new(0x00, 0xe5, 0xff), 0.625f),   // narrowest
        (new(0x00, 0xcb, 0xff), 0.688f),
        (new(0x00, 0x96, 0xff), 0.750f),
        (new(0x00, 0x5f, 0xff), 0.813f),
        (new(0x15, 0x00, 0xff), 0.875f),
        (new(0x11, 0x00, 0xe9), 0.938f),
        (new(0x0e, 0x00, 0xcf), 1.000f),
        (new(0x08, 0x00, 0x9b), 1.063f),
        (new(0x03, 0x00, 0x68), 1.125f),
        (new(0x00, 0x58, 0x00), 1.188f),
        (new(0x00, 0x81, 0x00), 1.250f),
        (new(0x00, 0xa3, 0x00), 1.313f),
        (new(0x00, 0xd4, 0x00), 1.375f),   // widest
    ];

    public static float GetHalfWidth(int paletteIndex) =>
        paletteIndex >= 0 && paletteIndex < WidthPalette.Length ? WidthPalette[paletteIndex].HalfWidth : 0.625f;

    public static int GetPaletteIndex(Rgba32 color)
    {
        for (int i = 0; i < WidthPalette.Length; i++)
            if (ColorMatch(WidthPalette[i].Color, color))
                return i;
        return -1;
    }

    private const int ColorTolerance = 2;

    private static bool ColorMatch(Rgba32 a, Rgba32 b) =>
        Math.Abs(a.R - b.R) <= ColorTolerance && Math.Abs(a.G - b.G) <= ColorTolerance && Math.Abs(a.B - b.B) <= ColorTolerance;

    private static readonly Rgba32 SourceColor = new(0, 255, 0);
    private static readonly Rgba32 ConfluenceColor = new(255, 0, 0);
    private static readonly Rgba32 BifurcationColor = new(255, 252, 0);
    private static readonly Rgba32 OceanColor = new(255, 0, 128);

    public static RiverCell FromRgba32(Rgba32 p)
    {
        int paletteIndex = GetPaletteIndex(p);
        if (paletteIndex >= 0)
            return new(RiverPixelType.River, (byte)paletteIndex);
        if (ColorMatch(SourceColor, p))
            return new(RiverPixelType.Source);
        if (ColorMatch(ConfluenceColor, p))
            return new(RiverPixelType.Confluence);
        if (ColorMatch(BifurcationColor, p))
            return new(RiverPixelType.Bifurcation);
        if (ColorMatch(OceanColor, p))
            return new(RiverPixelType.Ocean);
        return new(RiverPixelType.Land);
    }

    public bool IsFilled => Type is not (RiverPixelType.Land or RiverPixelType.Ocean);
}

public enum SegmentEndKind
{
    None,
    Source,
    Confluence,
    Bifurcation,
}
