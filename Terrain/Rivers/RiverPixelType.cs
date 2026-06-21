#nullable enable

using System;
using SixLabors.ImageSharp.PixelFormats;

namespace Terrain.Rivers;

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
    private static readonly Rgba32[] WidthPalette =
    [
        new(0x00, 0xe1, 0xff),   // width index 0
        new(0x00, 0xc8, 0xff),   // width index 1
        new(0x00, 0x96, 0xff),   // width index 2
        new(0x00, 0x64, 0xff),   // width index 3
        new(0x00, 0x00, 0xff),   // width index 4
        new(0x00, 0x00, 0xe1),   // width index 5
        new(0x00, 0x00, 0xc8),   // width index 6
        new(0x00, 0x00, 0x96),   // width index 7
        new(0x00, 0x00, 0x64),   // width index 8
        new(0x00, 0x7d, 0x00),   // width index 9
        new(0x18, 0xce, 0x00),   // width index 10
    ];

    public static int WidthPaletteCount => WidthPalette.Length;

    public static float GetWidthFactor(int paletteIndex)
    {
        if (WidthPalette.Length <= 1)
            return 0.0f;

        int clamped = Math.Clamp(paletteIndex, 0, WidthPalette.Length - 1);
        return clamped / (float)(WidthPalette.Length - 1);
    }

    public static float GetHalfWidth(int paletteIndex, float minFullWidth, float maxFullWidth)
    {
        if (!float.IsFinite(minFullWidth) || minFullWidth <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(minFullWidth), "River min width must be greater than 0.");
        if (!float.IsFinite(maxFullWidth) || maxFullWidth < minFullWidth)
            throw new ArgumentOutOfRangeException(nameof(maxFullWidth), "River max width must be greater than or equal to min width.");

        float fullWidth = minFullWidth + (maxFullWidth - minFullWidth) * GetWidthFactor(paletteIndex);
        return fullWidth * 0.5f;
    }

    public static int GetPaletteIndex(Rgba32 color)
    {
        for (int i = 0; i < WidthPalette.Length; i++)
            if (ColorMatch(WidthPalette[i], color))
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
