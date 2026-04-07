#nullable enable

using System;
using System.IO;
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
        TextureSize targetSize = TextureSize.Size512,
        bool isNormalMap = false)
    {
        var rgbaData = LoadAndResize(filePath, (int)targetSize);
        if (rgbaData == null)
            return null;

        return CreateTextureFromBytes(device, rgbaData, (int)targetSize, isNormalMap);
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
    public static Texture CreateTextureFromBytes(GraphicsDevice device, byte[] rgbaData, int size, bool isNormalMap = false)
    {
        PixelFormat pixelFormat = isNormalMap
            ? PixelFormat.R8G8B8A8_UNorm
            : PixelFormat.R8G8B8A8_UNorm_SRgb;

        return Texture.New2D(
            device,
            size,
            size,
            pixelFormat,
            rgbaData,
            TextureFlags.ShaderResource);
    }

    /// <summary>
    /// 检查文件是否为支持的图像格式。
    /// </summary>
    public static bool IsSupportedImageFormat(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".tiff" or ".tif";
    }

    /// <summary>
    /// 根据 Albedo 纹理路径查找匹配的法线贴图。
    /// 支持常见的命名约定：_normal, _n, _Normal 等。
    /// </summary>
    /// <param name="albedoPath">Albedo 纹理文件路径。</param>
    /// <returns>法线贴图路径，未找到返回 null。</returns>
    public static string? FindMatchingNormalMap(string albedoPath)
    {
        if (!File.Exists(albedoPath))
            return null;

        string directory = Path.GetDirectoryName(albedoPath) ?? "";
        string nameWithoutExt = Path.GetFileNameWithoutExtension(albedoPath);
        string ext = Path.GetExtension(albedoPath);

        // 支持的图像扩展名（用于候选文件检查）
        string[] supportedExts = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tiff", ".tif" };

        // 常见 Diffuse 后缀，需要替换为 Normal 后缀
        string[] diffuseSuffixes = {
            "_diffuse", "_Diffuse", "_DIFFUSE",
            "_albedo", "_Albedo", "_ALBEDO",
            "_basecolor", "_BaseColor", "_BaseColor", "_baseColor",
            "_color", "_Color", "_COLOR",
            "_d", "_D"
        };

        // Normal 后缀候选
        string[] normalSuffixes = {
            "_normal", "_Normal", "_NORMAL",
            "_n", "_N",
            "_normalmap", "_NormalMap", "_Normalmap"
        };

        // Normal 前缀候选
        string[] normalPrefixes = {
            "Normal_", "normal_", "NORMAL_"
        };

        // 1. 尝试替换 Diffuse 后缀为 Normal 后缀
        foreach (var ds in diffuseSuffixes)
        {
            if (nameWithoutExt.EndsWith(ds, StringComparison.OrdinalIgnoreCase))
            {
                string baseName = nameWithoutExt[..^ds.Length];
                foreach (var ns in normalSuffixes)
                {
                    foreach (var e in supportedExts)
                    {
                        string candidate = Path.Combine(directory, baseName + ns + e);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }
        }

        // 2. 尝试直接添加 Normal 后缀
        foreach (var ns in normalSuffixes)
        {
            foreach (var e in supportedExts)
            {
                string candidate = Path.Combine(directory, nameWithoutExt + ns + e);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        // 3. 尝试 Normal 子目录
        string normalSubdir = Path.Combine(directory, "Normal");
        string normalSubdirLower = Path.Combine(directory, "normal");

        foreach (var subdir in new[] { normalSubdir, normalSubdirLower })
        {
            if (Directory.Exists(subdir))
            {
                foreach (var e in supportedExts)
                {
                    string candidate = Path.Combine(subdir, nameWithoutExt + e);
                    if (File.Exists(candidate))
                        return candidate;
                }

                // 也尝试带 _normal 后缀
                foreach (var ns in normalSuffixes)
                {
                    foreach (var e in supportedExts)
                    {
                        string candidate = Path.Combine(subdir, nameWithoutExt + ns + e);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }
        }

        return null;
    }
}
