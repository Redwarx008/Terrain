#nullable enable

using System;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

/// <summary>
/// 索引颜色河流地图数据存储。
/// 交织格式 [type_byte, width_byte] 每像素 2 字节。
/// type 对应 RiverPixelType, width 为蓝通道宽度值 (0-12)。
/// </summary>
public sealed class RiverMap
{
    private const int BytesPerPixel = 2;
    private readonly byte[] data;

    public int Width { get; }
    public int Height { get; }

    public RiverMap(int width, int height, RiverPixelType defaultType = RiverPixelType.Land, byte defaultWidth = 0)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        data = new byte[width * height * BytesPerPixel];
        byte defaultTypeByte = (byte)defaultType;
        for (int i = 0; i < data.Length; i += BytesPerPixel)
        {
            data[i] = defaultTypeByte;
            data[i + 1] = defaultWidth;
        }
    }

    /// <summary>获取像素类型</summary>
    public RiverPixelType GetType(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return RiverPixelType.Land;
        return (RiverPixelType)data[(y * Width + x) * BytesPerPixel];
    }

    /// <summary>获取宽度值（蓝通道，0-12）</summary>
    public byte GetWidth(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return 0;
        return data[(y * Width + x) * BytesPerPixel + 1];
    }

    public void SetPixel(int x, int y, RiverPixelType type, byte width = 0)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;
        int index = (y * Width + x) * BytesPerPixel;
        data[index] = (byte)type;
        data[index + 1] = type == RiverPixelType.River ? width : (byte)0;
    }

    /// <summary>判断该像素是否非陆地（用于 IsFilled 检查）</summary>
    public bool IsFilled(int x, int y) => GetType(x, y) != RiverPixelType.Land;

    /// <summary>获取原始数据引用（给编辑命令等使用）</summary>
    public byte[] GetRawData() => data;

    public void Clear(RiverPixelType type = RiverPixelType.Land, byte width = 0)
    {
        byte typeByte = (byte)type;
        for (int i = 0; i < data.Length; i += BytesPerPixel)
        {
            data[i] = typeByte;
            data[i + 1] = width;
        }
    }
}
