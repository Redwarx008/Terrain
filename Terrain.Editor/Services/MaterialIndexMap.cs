#nullable enable

using System;
using System.Runtime.InteropServices;

namespace Terrain.Editor.Services;

/// <summary>
/// 材质像素数据，包含 RGBA 四通道信息。
/// </summary>
public struct MaterialPixel
{
    public byte Index;
    public byte Weight;
    public byte Projection;
    public byte Rotation;

    public static readonly MaterialPixel Default = new()
    {
        Index = 0,
        Weight = 255,
        Projection = 0x77,
        Rotation = 0
    };

    /// <summary>
    /// 将像素打包为 uint32 (R=低字节, A=高字节)。
    /// </summary>
    public uint Pack() => (uint)(Index | (Weight << 8) | (Projection << 16) | (Rotation << 24));

    /// <summary>
    /// 从 uint32 解包像素。
    /// </summary>
    public static MaterialPixel Unpack(uint packed) => new()
    {
        Index = (byte)(packed & 0xFF),
        Weight = (byte)((packed >> 8) & 0xFF),
        Projection = (byte)((packed >> 16) & 0xFF),
        Rotation = (byte)((packed >> 24) & 0xFF)
    };
}

/// <summary>
/// 材质索引图，使用 uint[] 存储（每像素一个 uint），避免 byte[] 的 2GB 长度限制。
/// R: 材质索引, G: 权重, B: 投影方向, A: 旋转角度
/// </summary>
public sealed class MaterialIndexMap
{
    public int Width { get; }
    public int Height { get; }
    private readonly uint[] data;
    public const int BytesPerPixel = 4;

    public MaterialIndexMap(int width, int height)
    {
        Width = width;
        Height = height;
        data = new uint[(long)width * height];
        Fill(MaterialPixel.Default);
    }

    public MaterialPixel GetPixel(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return MaterialPixel.Default;
        return MaterialPixel.Unpack(data[z * Width + x]);
    }

    public void SetPixel(int x, int z, MaterialPixel pixel)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return;
        data[z * Width + x] = pixel.Pack();
    }

    public void SetIndex(int x, int z, byte materialIndex)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return;
        int idx = z * Width + x;
        data[idx] = (data[idx] & 0xFFFFFF00u) | materialIndex;
    }

    public byte GetIndex(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return 0;
        return (byte)(data[z * Width + x] & 0xFF);
    }

    /// <summary>
    /// 获取原始 uint 数据的直接引用。
    /// </summary>
    public uint[] GetRawData() => data;

    /// <summary>
    /// 获取指定切片区域的 uint 子 Span。
    /// </summary>
    public Span<uint> GetSliceSpan(int startX, int startZ, int sliceWidth, int sliceHeight)
    {
        return data.AsSpan(startZ * Width + startX, sliceHeight * sliceWidth);
    }

    /// <summary>
    /// 将指定切片区域的数据作为 byte Span 返回，用于 GPU 上传。
    /// 使用 MemoryMarshal 零拷贝转换。
    /// 注意：返回的是整行的 byte view，调用方需按行切片处理非连续行。
    /// </summary>
    public ReadOnlySpan<byte> GetSliceBytesPerRow(int startX, int startZ, int row, int sliceWidth)
    {
        int pixelOffset = (startZ + row) * Width + startX;
        var pixelSpan = data.AsSpan(pixelOffset, sliceWidth);
        return MemoryMarshal.AsBytes(pixelSpan);
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
            var srcPixels = MemoryMarshal.Cast<byte, uint>(bytes.Slice(srcByteOffset, regionWidth * BytesPerPixel));
            srcPixels.CopyTo(data.AsSpan(dstOffset, regionWidth));
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
            var srcPixels = data.AsSpan(srcOffset, regionWidth);
            var dstBytes = MemoryMarshal.AsBytes(srcPixels);
            dstBytes.CopyTo(bytes.AsSpan(row * regionWidth * BytesPerPixel));
        }
        return bytes;
    }

    public void Clear() => Fill(MaterialPixel.Default);

    public void Fill(MaterialPixel pixel)
    {
        Array.Fill(data, pixel.Pack());
    }

    public void Fill(byte materialIndex)
    {
        Fill(new MaterialPixel { Index = materialIndex, Weight = 255, Projection = 0x77, Rotation = 0 });
    }

    public void MigrateFromR8(byte[] oldData)
    {
        if (oldData.Length != (long)Width * Height)
            throw new ArgumentException("Old data size mismatch.", nameof(oldData));

        uint defaultPixel = new MaterialPixel { Weight = 255, Projection = 0x77 }.Pack();
        for (int i = 0; i < oldData.Length; i++)
        {
            data[i] = (defaultPixel & 0xFFFFFF00u) | oldData[i];
        }
    }

    #region 投影方向编码工具

    public static byte EncodeProjectionDirection(float normalX, float normalY, float normalZ)
    {
        if (MathF.Abs(normalY) < 0.0001f)
            return 0x77;

        float projX = normalX / normalY;
        float projZ = normalZ / normalY;
        int encX = Math.Clamp((int)MathF.Floor(projX * 7.0f + 7.5f), 0, 15);
        int encZ = Math.Clamp((int)MathF.Floor(projZ * 7.0f + 7.5f), 0, 15);
        return (byte)(encZ * 16 + encX);
    }

    public static byte EncodeProjectionDirection(System.Numerics.Vector3 normal)
    {
        return EncodeProjectionDirection(normal.X, normal.Y, normal.Z);
    }

    #endregion
}
