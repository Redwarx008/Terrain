#nullable enable

using System;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

/// <summary>
/// 河流地图颜色编解码，严格匹配 CK3 索引颜色规范。
/// 蓝通道宽度值范围 0-12（共 13 档）。
/// </summary>
public static class RiverColorConverter
{
    /// <summary>河宽档位数，对应 NUM_WIDTH_PIXEL_VALUES = 13</summary>
    public const int NumWidthValues = 13;

    // 精准颜色匹配（含容差）
    private const int ColorTolerance = 2;

    public static void Encode(RiverPixelType type, byte width, out byte r, out byte g, out byte b)
    {
        switch (type)
        {
            case RiverPixelType.Land:
                r = 255; g = 255; b = 255; break;
            case RiverPixelType.River:
                r = 0; g = 0; b = Math.Min(width, (byte)12); break;
            case RiverPixelType.Source:
                r = 0; g = 255; b = 0; break;
            case RiverPixelType.Confluence:
                r = 255; g = 0; b = 0; break;
            case RiverPixelType.Bifurcation:
                r = 255; g = 252; b = 0; break;
            case RiverPixelType.Ocean:
                r = 255; g = 0; b = 128; break;
            default:
                r = 255; g = 255; b = 255; break;
        }
    }

    public static (RiverPixelType Type, byte Width) Decode(byte r, byte g, byte b)
    {
        // River (蓝色渐变): R=0, G=0, B=0-12
        if (r == 0 && g == 0 && b <= 12)
            return (RiverPixelType.River, b);

        // Source: #00FF00
        if (IsColorMatch(r, g, b, 0, 255, 0))
            return (RiverPixelType.Source, 0);

        // Confluence: #FF0000
        if (IsColorMatch(r, g, b, 255, 0, 0))
            return (RiverPixelType.Confluence, 0);

        // Bifurcation: #FFFC00
        if (IsColorMatch(r, g, b, 255, 252, 0))
            return (RiverPixelType.Bifurcation, 0);

        // Ocean: #FF0080
        if (IsColorMatch(r, g, b, 255, 0, 128))
            return (RiverPixelType.Ocean, 0);

        // 默认 Land (白色/其他)
        return (RiverPixelType.Land, 0);
    }

    /// <summary>检测是否为灰度图像（所有像素 R≈G≈B），用于旧格式兼容</summary>
    public static bool IsGrayscale(byte r, byte g, byte b)
    {
        return Math.Abs(r - g) <= ColorTolerance && Math.Abs(g - b) <= ColorTolerance;
    }

    /// <summary>将世界空间宽度 (WIDTH_MIN~WIDTH_MAX) 映射到蓝通道值 (0-12)</summary>
    public static byte WorldWidthToBlueValue(float worldWidth, float widthMin = 1.0f, float widthMax = 4.0f)
    {
        float t = (worldWidth - widthMin) / (widthMax - widthMin);
        return (byte)Math.Clamp(MathF.Round(t * 12.0f), 0, 12);
    }

    /// <summary>将蓝通道值 (0-12) 映射到世界空间半宽</summary>
    public static float BlueValueToHalfWidth(byte blueValue, float widthMin = 1.0f, float widthMax = 4.0f)
    {
        float t = blueValue / 12.0f;
        return Math.Max(widthMin, MathUtil.Lerp(widthMin, widthMax, t)) * 0.5f;
    }

    private static bool IsColorMatch(byte r, byte g, byte b, byte targetR, byte targetG, byte targetB)
    {
        return Math.Abs(r - targetR) <= ColorTolerance
            && Math.Abs(g - targetG) <= ColorTolerance
            && Math.Abs(b - targetB) <= ColorTolerance;
    }
}

file static class MathUtil
{
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
