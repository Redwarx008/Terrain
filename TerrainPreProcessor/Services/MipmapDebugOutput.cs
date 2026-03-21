using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;

namespace TerrainPreProcessor.Services;

/// <summary>
/// Mipmap 调试输出服务
/// 仅在 DEBUG 构建时启用，用于保存每一级 mipmap 的 PNG 图像
/// </summary>
public static class MipmapDebugOutput
{
    /// <summary>
    /// 保存 mipmap 层级图像为 PNG
    /// </summary>
    public static void SaveMipmapLevel<TPixel>(
        Image<TPixel> image,
        string outputDirectory,
        string prefix,
        int mipLevel)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // 文件名：前缀_MipXX_尺寸.png
        string fileName = $"{prefix}_Mip{mipLevel:D2}_{image.Width}x{image.Height}.png";
        string filePath = Path.Combine(outputDirectory, fileName);

        image.SaveAsPng(filePath);
        Console.WriteLine($"[Debug] 已保存 {prefix} mipmap 层级 {mipLevel}: {filePath}");
    }
}
