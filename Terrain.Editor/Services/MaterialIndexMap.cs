#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// 材质像素数据，包含 RGBA 四通道信息。
/// </summary>
public struct MaterialPixel
{
    /// <summary>
    /// 材质索引 (0-255)。
    /// </summary>
    public byte Index;

    /// <summary>
    /// 权重 (0-255)，控制混合权重和过渡位置。
    /// 0 = 边缘，255 = 中心。
    /// </summary>
    public byte Weight;

    /// <summary>
    /// 投影方向编码 (4:4 格式)。
    /// 用于 3D 投影，解决悬崖纹理拉伸问题。
    /// 0x77 = 默认向上 (0, 1, 0)。
    /// </summary>
    public byte Projection;

    /// <summary>
    /// 旋转角度 (0-255 = 0°-360°)。
    /// 用于打破纹理平铺重复。
    /// </summary>
    public byte Rotation;

    /// <summary>
    /// 默认像素值：索引0，权重最大，向上投影，无旋转。
    /// </summary>
    public static readonly MaterialPixel Default = new()
    {
        Index = 0,
        Weight = 255,
        Projection = 0x77, // 默认向上
        Rotation = 0
    };
}

/// <summary>
/// 材质索引图，存储每个像素的 RGBA 数据。
/// R: 材质索引 (0-255)
/// G: 权重 (0-255)
/// B: 投影方向编码 (4:4 格式)
/// A: 旋转角度 (0-255 = 0°-360°)
/// </summary>
public sealed class MaterialIndexMap
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// 原始 RGBA 数据，每像素 4 字节。
    /// </summary>
    private readonly byte[] data;

    /// <summary>
    /// 每像素字节数 (RGBA = 4)。
    /// </summary>
    public const int BytesPerPixel = 4;

    public MaterialIndexMap(int width, int height)
    {
        Width = width;
        Height = height;
        data = new byte[width * height * BytesPerPixel];

        // 初始化为默认值
        Fill(MaterialPixel.Default);
    }

    /// <summary>
    /// 获取指定位置的材质像素数据。
    /// </summary>
    public MaterialPixel GetPixel(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return MaterialPixel.Default;

        int offset = (z * Width + x) * BytesPerPixel;
        return new MaterialPixel
        {
            Index = data[offset],
            Weight = data[offset + 1],
            Projection = data[offset + 2],
            Rotation = data[offset + 3]
        };
    }

    /// <summary>
    /// 设置指定位置的材质像素数据。
    /// </summary>
    public void SetPixel(int x, int z, MaterialPixel pixel)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return;

        int offset = (z * Width + x) * BytesPerPixel;
        data[offset] = pixel.Index;
        data[offset + 1] = pixel.Weight;
        data[offset + 2] = pixel.Projection;
        data[offset + 3] = pixel.Rotation;
    }

    /// <summary>
    /// 设置指定位置的材质索引（保持其他通道不变）。
    /// 兼容旧接口。
    /// </summary>
    public void SetIndex(int x, int z, byte materialIndex)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return;

        int offset = (z * Width + x) * BytesPerPixel;
        data[offset] = materialIndex;
        // 保持其他通道不变
    }

    /// <summary>
    /// 获取指定位置的材质索引。
    /// 兼容旧接口。
    /// </summary>
    public byte GetIndex(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return 0;

        int offset = (z * Width + x) * BytesPerPixel;
        return data[offset];
    }

    /// <summary>
    /// 获取原始 RGBA 数据用于 GPU 上传。
    /// </summary>
    public byte[] GetRawData() => data;

    /// <summary>
    /// 清空所有数据为默认值。
    /// </summary>
    public void Clear()
    {
        Fill(MaterialPixel.Default);
    }

    /// <summary>
    /// 填充所有像素为指定像素值。
    /// </summary>
    public void Fill(MaterialPixel pixel)
    {
        for (int i = 0; i < data.Length; i += BytesPerPixel)
        {
            data[i] = pixel.Index;
            data[i + 1] = pixel.Weight;
            data[i + 2] = pixel.Projection;
            data[i + 3] = pixel.Rotation;
        }
    }

    /// <summary>
    /// 填充所有像素为指定索引（保持其他通道为默认值）。
    /// 兼容旧接口。
    /// </summary>
    public void Fill(byte materialIndex)
    {
        var pixel = new MaterialPixel
        {
            Index = materialIndex,
            Weight = 255,
            Projection = 0x77,
            Rotation = 0
        };
        Fill(pixel);
    }

    /// <summary>
    /// 从旧格式 (R8) 数据迁移。
    /// </summary>
    /// <param name="oldData">旧格式数据，每像素 1 字节。</param>
    public void MigrateFromR8(byte[] oldData)
    {
        if (oldData.Length != Width * Height)
            throw new ArgumentException("Old data size mismatch.", nameof(oldData));

        for (int i = 0; i < oldData.Length; i++)
        {
            int offset = i * BytesPerPixel;
            data[offset] = oldData[i];      // R: 索引
            data[offset + 1] = 255;          // G: 权重最大
            data[offset + 2] = 0x77;         // B: 默认向上投影
            data[offset + 3] = 0;            // A: 无旋转
        }
    }

    #region 投影方向编码工具

    /// <summary>
    /// 编码投影方向到单字节 (4:4 格式)。
    /// </summary>
    /// <param name="normalX">法线 X 分量。</param>
    /// <param name="normalY">法线 Y 分量 (应该 > 0)。</param>
    /// <param name="normalZ">法线 Z 分量。</param>
    /// <returns>编码后的投影方向 (0-255)。</returns>
    public static byte EncodeProjectionDirection(float normalX, float normalY, float normalZ)
    {
        // 防止除零
        if (MathF.Abs(normalY) < 0.0001f)
            return 0x77; // 默认向上

        // 计算投影方向
        float projX = normalX / normalY;
        float projZ = normalZ / normalY;

        // 缩放到 [0, 15] 范围
        int encX = Math.Clamp((int)MathF.Floor(projX * 7.0f + 7.5f), 0, 15);
        int encZ = Math.Clamp((int)MathF.Floor(projZ * 7.0f + 7.5f), 0, 15);

        // 4:4 编码
        return (byte)(encZ * 16 + encX);
    }

    /// <summary>
    /// 编码投影方向到单字节 (4:4 格式)。
    /// </summary>
    /// <param name="normal">法线向量。</param>
    /// <returns>编码后的投影方向 (0-255)。</returns>
    public static byte EncodeProjectionDirection(System.Numerics.Vector3 normal)
    {
        return EncodeProjectionDirection(normal.X, normal.Y, normal.Z);
    }

    #endregion
}
