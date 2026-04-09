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
    public static readonly Color4 Background = Srgb(0x21, 0x25, 0x2B);       // #21252B
    public static readonly Color4 PanelBackground = Srgb(0x28, 0x2C, 0x34);  // #282C34
    public static readonly Color4 DarkBackground = Srgb(0x21, 0x25, 0x2B);   // #21252B

    // Interaction colors
    public static readonly Color4 Selection = Srgb(0x3D, 0x44, 0x50);          // #3D4450
    public static readonly Color4 SelectionInactive = Srgb(0x2C, 0x31, 0x3A);  // #2C313A
    public static readonly Color4 Hover = Srgb(0x3D, 0x44, 0x50);              // #3D4450
    public static readonly Color4 HoverLight = Srgb(0x4B, 0x53, 0x62);         // #4B5362
    public static readonly Color4 Pressed = Srgb(0x1D, 0x20, 0x26);            // #1D2026

    // Borders and separators
    public static readonly Color4 Border = Srgb(0x1A, 0x1D, 0x22);       // #1A1D22
    public static readonly Color4 BorderLight = Srgb(0x41, 0x47, 0x53);  // #414753
    public static readonly Color4 BorderDark = Srgb(0x1A, 0x1D, 0x22);   // #1A1D22

    // Text colors
    public static readonly Color4 TextPrimary = Srgb(0xE0, 0xE0, 0xE0);    // #E0E0E0
    public static readonly Color4 TextSecondary = Srgb(0x8E, 0x91, 0x94);  // #8E9194
    public static readonly Color4 TextDisabled = Srgb(0x5D, 0x60, 0x63);   // #5D6063
    public static readonly Color4 TextHighlight = Srgb(0xFF, 0xFF, 0xFF);  // #FFFFFF

    // Accent colors
    public static readonly Color4 Accent = Srgb(0x47, 0x8E, 0xFF);         // #478EFF
    public static readonly Color4 AccentHover = Srgb(0x62, 0x9E, 0xFF);    // #629EFF
    public static readonly Color4 AccentPressed = Srgb(0x2F, 0x7F, 0xE8);  // #2F7FE8

    // Status colors
    public static readonly Color4 Success = Srgb(0x22, 0xD3, 0xEE);  // #22D3EE
    public static readonly Color4 Warning = Srgb(0xFF, 0xE6, 0x6D);  // #FFE66D
    public static readonly Color4 Error = Srgb(0xFF, 0x5F, 0x5F);    // #FF5F5F
    public static readonly Color4 Info = Srgb(0x47, 0x8E, 0xFF);     // #478EFF

    // Control-specific colors
    public static readonly Color4 ButtonDefault = Srgb(0x42, 0x42, 0x42);  // #424242
    public static readonly Color4 ButtonHover = Srgb(0x4D, 0x4D, 0x4D);    // #4D4D4D
    public static readonly Color4 ButtonPressed = Srgb(0x47, 0x8E, 0xFF);  // #478EFF

    public static readonly Color4 InputBackground = Srgb(0x21, 0x25, 0x2B);  // #21252B
    public static readonly Color4 InputBorder = Srgb(0x21, 0x25, 0x2B);      // #21252B
    public static readonly Color4 InputFocus = Srgb(0x47, 0x8E, 0xFF);       // #478EFF

    public static readonly Color4 ScrollbarTrack = Srgb(0x1D, 0x20, 0x26);      // #1D2026
    public static readonly Color4 ScrollbarThumb = Srgb(0x3D, 0x44, 0x50);      // #3D4450
    public static readonly Color4 ScrollbarThumbHover = Srgb(0x4B, 0x53, 0x62); // #4B5362

    // Title bar colors
    public static readonly Color4 TitleBar = Srgb(0x21, 0x25, 0x2B);        // #21252B
    public static readonly Color4 TitleBarActive = Srgb(0x2C, 0x31, 0x3A);  // #2C313A

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
