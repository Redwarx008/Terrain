using System;
using Stride.Graphics;

namespace Terrain.Utilities;

/// <summary>
/// 为块压缩纹理格式 (BC1/BC3) 提供 CPU 端编码工具。
/// </summary>
public static class TextureBlockEncoder
{
    /// <summary>
    /// 生成平坦法线 (128,128,255,255) 的纹理数据（含所有 mip 级别）。
    /// 支持非压缩格式和 BC1/BC3 压缩格式。
    /// </summary>
    public static byte[][] CreateFlatNormalMipData(PixelFormat format, int width, int height, int mipCount)
    {
        var mipData = new byte[mipCount][];
        for (int mip = 0; mip < mipCount; mip++)
        {
            int mipW = Math.Max(1, width >> mip);
            int mipH = Math.Max(1, height >> mip);
            mipData[mip] = format.IsCompressed
                ? CreateCompressedMipData(format, mipW, mipH)
                : CreateUncompressedMipData(mipW, mipH);
        }
        return mipData;
    }

    private static byte[] CreateUncompressedMipData(int mipW, int mipH)
    {
        var data = new byte[mipW * mipH * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = 128; data[i + 1] = 128;
            data[i + 2] = 255; data[i + 3] = 255;
        }
        return data;
    }

    private static byte[] CreateCompressedMipData(PixelFormat format, int mipW, int mipH)
    {
        int blockCountX = Math.Max(1, (mipW + 3) / 4);
        int blockCountY = Math.Max(1, (mipH + 3) / 4);
        int bytesPerBlock = format.BlockSize;
        int totalBlocks = blockCountX * blockCountY;

        Span<byte> template = stackalloc byte[bytesPerBlock];
        EncodeFlatNormalBlock(format, template);

        var data = new byte[totalBlocks * bytesPerBlock];
        var dataSpan = data.AsSpan();
        for (int i = 0; i < totalBlocks; i++)
            template.CopyTo(dataSpan.Slice(i * bytesPerBlock, bytesPerBlock));

        return data;
    }

    /// <summary>
    /// 编码单个 4x4 像素块，所有像素为平坦法线 (128,128,255,255)。
    /// </summary>
    private static void EncodeFlatNormalBlock(PixelFormat format, Span<byte> block)
    {
        if (format == PixelFormat.BC3_UNorm || format == PixelFormat.BC3_UNorm_SRgb)
        {
            var alphaPart = block.Slice(0, 8);
            var colorPart = block.Slice(8, 8);

            // Alpha: ref0=255, ref1=255, indices=0 → 解码为 255
            alphaPart[0] = 255;
            alphaPart[1] = 255;

            EncodeBC1Color(128, 128, 255, colorPart);
        }
        else if (format == PixelFormat.BC1_UNorm || format == PixelFormat.BC1_UNorm_SRgb)
        {
            EncodeBC1Color(128, 128, 255, block);
        }
        else
        {
            block.Clear();
        }
    }

    /// <summary>
    /// BC1 颜色编码：RGB → RGB565，两个参考颜色相同 → 索引全零 → 单一颜色。
    /// </summary>
    private static void EncodeBC1Color(int r, int g, int b, Span<byte> block)
    {
        ushort c565 = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
        block[0] = (byte)(c565 & 0xFF);
        block[1] = (byte)(c565 >> 8);
        block[2] = block[0]; // color1 = color0
        block[3] = block[1];
        // 索引 4 bytes 已默认为零
    }
}
