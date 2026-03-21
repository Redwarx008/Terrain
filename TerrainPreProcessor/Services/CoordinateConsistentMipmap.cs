using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace TerrainPreProcessor.Services;

/// <summary>
/// 坐标一致性 Mipmap 生成器
/// 确保父子层级间的位置对应关系，不进行滤波混合，保留原始数据精确值
/// </summary>
public static class CoordinateConsistentMipmap
{
    /// <summary>
    /// 生成下一级 mipmap（坐标一致性版本）
    /// 核心算法：
    /// - 偶数尺寸：父层级(x, y) = 子层级(2x, 2y)
    /// - 奇数尺寸：父层级(x, y) = 子层级(2x, 2y)，边缘复制相邻像素
    /// </summary>
    public static Image<TPixel> GenerateNextMip<TPixel>(Image<TPixel> source) where TPixel : unmanaged, IPixel<TPixel>
    {
        int srcW = source.Width;
        int srcH = source.Height;

        int dstW = (srcW + 1) / 2;
        int dstH = (srcH + 1) / 2;

        var result = new Image<TPixel>(dstW, dstH);

        source.ProcessPixelRows(result, (srcAccessor, dstAccessor) =>
        {
            for (int dy = 0; dy < dstH; dy++)
            {
                var dstRow = dstAccessor.GetRowSpan(dy);
                int sy = dy * 2;

                if (sy >= srcH) sy = srcH - 1;

                var srcRow = srcAccessor.GetRowSpan(sy);

                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx = dx * 2;
                    if (sx >= srcW) sx = srcW - 1;

                    dstRow[dx] = srcRow[sx];
                }
            }
        });

        return result;
    }

    /// <summary>
    /// 生成所有 mipmap 层级
    /// </summary>
    public static Image<TPixel>[] GenerateAllMips<TPixel>(Image<TPixel> source) where TPixel : unmanaged, IPixel<TPixel>
    {
        // 计算需要的 mipmap 层级数
        int maxDimension = Math.Max(source.Width, source.Height);
        int mipLevels = (int)Math.Ceiling(Math.Log2(maxDimension)) + 1;

        var mips = new Image<TPixel>[mipLevels];
        mips[0] = source;

        for (int i = 1; i < mipLevels; i++)
        {
            mips[i] = GenerateNextMip(mips[i - 1]);
        }

        return mips;
    }

    /// <summary>
    /// 计算 mipmap 层级数
    /// </summary>
    public static int CalculateMipLevels(int width, int height, int minTileSize)
    {
        int maxDimension = Math.Max(width, height);
        int levels = 0;

        while (maxDimension > minTileSize)
        {
            maxDimension = (maxDimension + 1) / 2;
            levels++;
        }

        return levels + 1; // +1 因为包含原始层级
    }

    /// <summary>
    /// 验证父子层级的坐标对应关系
    /// </summary>
    public static bool ValidateCoordinateConsistency<TPixel>(Image<TPixel> parent, Image<TPixel> child) where TPixel : unmanaged, IPixel<TPixel>
    {
        int expectedParentW = (child.Width + 1) / 2;
        int expectedParentH = (child.Height + 1) / 2;

        if (parent.Width != expectedParentW || parent.Height != expectedParentH)
            return false;

        // 抽样检查几个点的对应关系
        int sampleCount = Math.Min(10, parent.Width * parent.Height);

        for (int i = 0; i < sampleCount; i++)
        {
            int px = (i * parent.Width) % parent.Width;
            int py = (i * parent.Height) % parent.Height;

            int cx = px * 2;
            int cy = py * 2;

            if (cx >= child.Width) cx = child.Width - 1;
            if (cy >= child.Height) cy = child.Height - 1;

            var parentPixel = parent[px, py];
            var childPixel = child[cx, cy];

            if (!parentPixel.Equals(childPixel))
                return false;
        }

        return true;
    }
}
