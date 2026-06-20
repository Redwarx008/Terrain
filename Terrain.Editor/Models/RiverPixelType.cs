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
        (new(0x00, 0xe1, 0xff), 0.500f),   // width index 0, full width 1.00
        (new(0x00, 0xc8, 0xff), 0.625f),   // width index 1, full width 1.25
        (new(0x00, 0x96, 0xff), 0.750f),   // width index 2, full width 1.50
        (new(0x00, 0x64, 0xff), 0.875f),   // width index 3, full width 1.75
        (new(0x00, 0x00, 0xff), 1.000f),   // width index 4, full width 2.00
        (new(0x00, 0x00, 0xe1), 1.125f),   // width index 5, full width 2.25
        (new(0x00, 0x00, 0xc8), 1.250f),   // width index 6, full width 2.50
        (new(0x00, 0x00, 0x96), 1.375f),   // width index 7, full width 2.75
        (new(0x00, 0x00, 0x64), 1.500f),   // width index 8, full width 3.00
        (new(0x00, 0x7d, 0x00), 1.625f),   // width index 9, full width 3.25
        (new(0x18, 0xce, 0x00), 1.750f),   // width index 10, full width 3.50
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
    private static readonly Rgba32 BifurcationColor = new(255, 252, 0);    private static readonly Rgba32 OceanColor = new(255, 0, 128);

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
