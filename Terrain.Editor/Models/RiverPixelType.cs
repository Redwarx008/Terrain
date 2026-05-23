#nullable enable

using System;
using SixLabors.ImageSharp.PixelFormats;

namespace Terrain.Editor.Models;

/// <summary>
/// 河流地图像素类型，对应索引颜色规范。
/// Land=白, River=蓝渐变(宽度编码在蓝通道), Source=绿, Confluence=红, Bifurcation=黄, Ocean=品红
/// </summary>
public enum RiverPixelType : byte
{
    Land = 0,
    River = 1,
    Source = 2,
    Confluence = 3,
    Bifurcation = 4,
    Ocean = 5,
}

/// <summary>
/// 河流地图的单一像素，内存中的唯一表示。
/// Type 决定像素语义，Width(0-12) 仅对 River 类型有效。
/// </summary>
public readonly record struct RiverCell(RiverPixelType Type, byte Width = 0)
{
    public Rgba32 ToRgba32()
    {
        return Type switch
        {
            RiverPixelType.Land => new(255, 255, 255, 255),
            RiverPixelType.River => new(0, 0, Math.Min(Width, (byte)12), 255),
            RiverPixelType.Source => new(0, 255, 0, 255),
            RiverPixelType.Confluence => new(255, 0, 0, 255),
            RiverPixelType.Bifurcation => new(255, 252, 0, 255),
            RiverPixelType.Ocean => new(255, 0, 128, 255),
            _ => new(255, 255, 255, 255),
        };
    }

    public static RiverCell FromRgba32(Rgba32 p)
    {
        if (p.R == 0 && p.G == 0 && p.B <= 12)
            return new(RiverPixelType.River, p.B);
        if (IsMatch(p, 0, 255, 0))
            return new(RiverPixelType.Source);
        if (IsMatch(p, 255, 0, 0))
            return new(RiverPixelType.Confluence);
        if (IsMatch(p, 255, 252, 0))
            return new(RiverPixelType.Bifurcation);
        if (IsMatch(p, 255, 0, 128))
            return new(RiverPixelType.Ocean);
        return new(RiverPixelType.Land);
    }

    /// <summary>旧格式 L8 灰度转换</summary>
    public static RiverCell LegacyFromGrayscale(byte v) =>
        v > 0 ? new(RiverPixelType.River, 12) : new(RiverPixelType.Land);

    /// <summary>检测灰度图像（旧格式兼容）</summary>
    public static bool IsGrayscale(Rgba32 p) =>
        Math.Abs(p.R - p.G) <= 2 && Math.Abs(p.G - p.B) <= 2;

    /// <summary>是否为"填充"像素（用于网格骨架化）</summary>
    public bool IsFilled => Type is not (RiverPixelType.Land or RiverPixelType.Ocean);

    private static bool IsMatch(Rgba32 p, byte r, byte g, byte b) =>
        Math.Abs(p.R - r) <= 2 && Math.Abs(p.G - g) <= 2 && Math.Abs(p.B - b) <= 2;
}
