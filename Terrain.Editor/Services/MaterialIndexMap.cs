#nullable enable

using System;
namespace Terrain.Editor.Services;

/// <summary>
/// 材质像素数据，仅包含材质索引。
/// </summary>
public struct MaterialPixel
{
    public byte Index;

    public static readonly MaterialPixel Default = new()
    {
        Index = 0,
    };
}

/// <summary>
/// 材质索引图，使用 byte[] 存储，每像素一个材质索引。
/// </summary>
public sealed class MaterialIndexMap
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] data;
    public const int BytesPerPixel = 1;

    public MaterialIndexMap(int width, int height)
    {
        Width = width;
        Height = height;
        data = new byte[(long)width * height];
        Fill(MaterialPixel.Default);
    }

    public MaterialPixel GetPixel(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return MaterialPixel.Default;
        return new MaterialPixel { Index = data[z * Width + x] };
    }

    public void SetPixel(int x, int z, MaterialPixel pixel)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return;
        data[z * Width + x] = pixel.Index;
    }

    public void SetIndex(int x, int z, byte materialIndex)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return;
        data[z * Width + x] = materialIndex;
    }

    public byte GetIndex(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return 0;
        return data[z * Width + x];
    }

    /// <summary>
    /// 获取原始 byte 数据的直接引用。
    /// </summary>
    public byte[] GetRawData() => data;

    /// <summary>
    /// 将指定切片区域的数据作为 byte Span 返回，用于 GPU 上传。
    /// </summary>
    public ReadOnlySpan<byte> GetSliceBytesPerRow(int startX, int startZ, int row, int sliceWidth)
    {
        int pixelOffset = (startZ + row) * Width + startX;
        return data.AsSpan(pixelOffset, sliceWidth);
    }

    /// <summary>
    /// 从 byte Span 写回指定区域（用于 undo/redo）。
    /// </summary>
    public void SetRegionFromBytes(int startX, int startZ, int regionWidth, int regionHeight, ReadOnlySpan<byte> bytes)
    {
        for (int row = 0; row < regionHeight; row++)
        {
            int dstOffset = (startZ + row) * Width + startX;
            int srcByteOffset = row * regionWidth * BytesPerPixel;
            bytes.Slice(srcByteOffset, regionWidth).CopyTo(data.AsSpan(dstOffset, regionWidth));
        }
    }

    /// <summary>
    /// 将指定区域的数据读取为 byte[]（用于 undo/redo before-capture）。
    /// </summary>
    public byte[] CopyRegionToBytes(int startX, int startZ, int regionWidth, int regionHeight)
    {
        var bytes = new byte[regionWidth * regionHeight * BytesPerPixel];
        for (int row = 0; row < regionHeight; row++)
        {
            int srcOffset = (startZ + row) * Width + startX;
            data.AsSpan(srcOffset, regionWidth).CopyTo(bytes.AsSpan(row * regionWidth * BytesPerPixel));
        }
        return bytes;
    }

    public void Clear() => Fill(MaterialPixel.Default);

    public void Fill(MaterialPixel pixel)
    {
        Array.Fill(data, pixel.Index);
    }

    public void Fill(byte materialIndex)
    {
        Fill(new MaterialPixel { Index = materialIndex });
    }

    public void MigrateFromR8(byte[] oldData)
    {
        if (oldData.Length != (long)Width * Height)
            throw new ArgumentException("Old data size mismatch.", nameof(oldData));

        oldData.CopyTo(data, 0);
    }
}
