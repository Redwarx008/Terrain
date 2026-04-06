#nullable enable

using System;
using Stride.Core.Mathematics;

namespace Terrain.Editor.UI.Styling;

/// <summary>
/// Editor color palette.
/// Colors are authored as sRGB bytes and converted to linear space,
/// so what you see on screen matches the intended hex values.
/// </summary>
public static class ColorPalette
{
    private static float SrgbByteToLinear(byte channel)
    {
        float srgb = channel / 255.0f;
        if (srgb <= 0.04045f)
            return srgb / 12.92f;

        return MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    }

    private static Color4 Srgb(byte r, byte g, byte b, float a = 1.0f)
    {
        return new Color4(
            SrgbByteToLinear(r),
            SrgbByteToLinear(g),
            SrgbByteToLinear(b),
            a);
    }

    // Background colors
    public static readonly Color4 Background = Srgb(0x1F, 0x1F, 0x1F);       // #1F1F1F
    public static readonly Color4 PanelBackground = Srgb(0x18, 0x18, 0x18);  // #181818
    public static readonly Color4 DarkBackground = Srgb(0x15, 0x15, 0x15);   // #151515

    // Interaction colors
    public static readonly Color4 Selection = Srgb(0x00, 0x78, 0xD4);          // #0078D4
    public static readonly Color4 SelectionInactive = Srgb(0x2B, 0x2B, 0x2B);  // #2B2B2B
    public static readonly Color4 Hover = Srgb(0x2B, 0x2B, 0x2B);              // #2B2B2B
    public static readonly Color4 HoverLight = Srgb(0x3C, 0x3C, 0x3C);         // #3C3C3C
    public static readonly Color4 Pressed = Srgb(0x02, 0x6E, 0xC1);            // #026EC1

    // Borders and separators
    public static readonly Color4 Border = Srgb(0x2B, 0x2B, 0x2B);       // #2B2B2B
    public static readonly Color4 BorderLight = Srgb(0x3C, 0x3C, 0x3C);  // #3C3C3C
    public static readonly Color4 BorderDark = Srgb(0x1A, 0x1A, 0x1A);   // #1A1A1A

    // Text colors
    public static readonly Color4 TextPrimary = Srgb(0xCC, 0xCC, 0xCC);    // #CCCCCC
    public static readonly Color4 TextSecondary = Srgb(0x9D, 0x9D, 0x9D);  // #9D9D9D
    public static readonly Color4 TextDisabled = Srgb(0x66, 0x66, 0x66);   // #666666
    public static readonly Color4 TextHighlight = Srgb(0xFF, 0xFF, 0xFF);  // #FFFFFF

    // Accent colors
    public static readonly Color4 Accent = Srgb(0x00, 0x78, 0xD4);         // #0078D4
    public static readonly Color4 AccentHover = Srgb(0x02, 0x6E, 0xC1);    // #026EC1
    public static readonly Color4 AccentPressed = Srgb(0x00, 0x5F, 0xA8);  // #005FA8

    // Status colors
    public static readonly Color4 Success = Srgb(0x4E, 0xC9, 0xB0);  // #4EC9B0
    public static readonly Color4 Warning = Srgb(0xCE, 0x91, 0x78);  // #CE9178
    public static readonly Color4 Error = Srgb(0xF4, 0x47, 0x47);    // #F44747
    public static readonly Color4 Info = Srgb(0x61, 0xAF, 0xEF);     // #61AFEF

    // Control-specific colors
    public static readonly Color4 ButtonDefault = Srgb(0x31, 0x31, 0x31);  // #313131
    public static readonly Color4 ButtonHover = Srgb(0x2B, 0x2B, 0x2B);    // #2B2B2B
    public static readonly Color4 ButtonPressed = Srgb(0x24, 0x24, 0x24);  // #242424

    public static readonly Color4 InputBackground = Srgb(0x31, 0x31, 0x31);  // #313131
    public static readonly Color4 InputBorder = Srgb(0x3C, 0x3C, 0x3C);      // #3C3C3C
    public static readonly Color4 InputFocus = Srgb(0x00, 0x78, 0xD4);       // #0078D4

    public static readonly Color4 ScrollbarTrack = Srgb(0x18, 0x18, 0x18);      // #181818
    public static readonly Color4 ScrollbarThumb = Srgb(0x61, 0x61, 0x61);      // #616161
    public static readonly Color4 ScrollbarThumbHover = Srgb(0x7E, 0x7E, 0x7E); // #7E7E7E

    // Title bar colors
    public static readonly Color4 TitleBar = Srgb(0x18, 0x18, 0x18);        // #181818
    public static readonly Color4 TitleBarActive = Srgb(0x18, 0x18, 0x18);  // #181818

    public static System.Numerics.Vector4 ToVector4(this Color4 color)
    {
        return new System.Numerics.Vector4(color.R, color.G, color.B, color.A);
    }

    public static uint ToUint(this Color4 color)
    {
        uint r = (uint)(color.R * 255.0f);
        uint g = (uint)(color.G * 255.0f);
        uint b = (uint)(color.B * 255.0f);
        uint a = (uint)(color.A * 255.0f);
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

    public static Color4 WithAlpha(this Color4 color, float alpha)
    {
        return new Color4(color.R, color.G, color.B, alpha);
    }
}