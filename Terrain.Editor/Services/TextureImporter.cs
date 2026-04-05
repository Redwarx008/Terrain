#nullable enable

using System;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Stride.Graphics;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace Terrain.Editor.Services;

/// <summary>
/// 纹理尺寸选项。
/// </summary>
public enum TextureSize
{
    Size512 = 512,
    Size1024 = 1024,
    Size2048 = 2048
}

/// <summary>
/// 纹理导入工具，支持从文件加载、缩放和创建 GPU 纹理。
/// </summary>
public static class TextureImporter
{
    /// <summary>
    /// 从文件导入纹理并缩放到指定尺寸。
    /// </summary>
    /// <param name="filePath">源文件路径（PNG/JPG/TGA/BMP）。</param>
    /// <param name="device">GraphicsDevice。</param>
    /// <param name="targetSize">目标尺寸。</param>
    /// <returns>导入的 Texture，失败返回 null。</returns>
    public static Texture? ImportFromFile(
        string filePath,
        GraphicsDevice device,
        TextureSize targetSize = TextureSize.Size512)
    {
        var rgbaData = LoadAndResize(filePath, (int)targetSize);
        if (rgbaData == null)
            return null;

        return CreateTextureFromBytes(device, rgbaData, (int)targetSize);
    }

    /// <summary>
    /// 加载图像文件并缩放到指定尺寸。
    /// </summary>
    /// <param name="filePath">源文件路径。</param>
    /// <param name="targetSize">目标尺寸。</param>
    /// <returns>RGBA 字节数组，失败返回 null。</returns>
    public static byte[]? LoadAndResize(string filePath, int targetSize)
    {
        try
        {
            using var original = ImageSharpImage.Load<Rgba32>(filePath);
            using var resized = original.Clone(ctx => ctx.Resize(targetSize, targetSize));

            var data = new byte[targetSize * targetSize * 4];
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    var pixel = resized[x, y];
                    int index = (y * targetSize + x) * 4;
                    data[index + 0] = pixel.R;
                    data[index + 1] = pixel.G;
                    data[index + 2] = pixel.B;
                    data[index + 3] = pixel.A;
                }
            }
            return data;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 从 RGBA 字节数组创建 GPU 纹理。
    /// </summary>
    public static Texture CreateTextureFromBytes(GraphicsDevice device, byte[] rgbaData, int size)
    {
        return Texture.New2D(
            device,
            size,
            size,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            rgbaData,
            TextureFlags.ShaderResource);
    }

    /// <summary>
    /// 检查文件是否为支持的图像格式。
    /// </summary>
    public static bool IsSupportedImageFormat(string filePath)
    {
        string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp";
    }
}
