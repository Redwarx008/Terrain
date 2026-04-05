#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// 材质索引图，存储每个像素的材质槽位索引 (0-255)。
/// 用于双线性过滤实现材质边缘过渡。
/// </summary>
public sealed class MaterialIndexMap
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] indices;

    public MaterialIndexMap(int width, int height)
    {
        Width = width;
        Height = height;
        indices = new byte[width * height];
    }

    /// <summary>
    /// 获取指定位置的材质索引。
    /// </summary>
    public byte GetIndex(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return 0;
        return indices[z * Width + x];
    }

    /// <summary>
    /// 设置指定位置的材质索引。
    /// </summary>
    public void SetIndex(int x, int z, byte materialIndex)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return;
        indices[z * Width + x] = materialIndex;
    }

    /// <summary>
    /// 获取原始数据用于 GPU 上传。
    /// </summary>
    public byte[] GetRawData() => indices;

    /// <summary>
    /// 清空所有索引为 0。
    /// </summary>
    public void Clear()
    {
        Array.Clear(indices, 0, indices.Length);
    }

    /// <summary>
    /// 填充所有像素为指定索引。
    /// </summary>
    public void Fill(byte materialIndex)
    {
        Array.Fill(indices, materialIndex);
    }
}
