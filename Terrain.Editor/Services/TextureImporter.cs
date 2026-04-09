#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Stride.Graphics;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace Terrain.Editor.Services;

/// <summary>
/// Texture import size options.
/// </summary>
public enum TextureSize
{
    Size512 = 512,
    Size1024 = 1024,
    Size2048 = 2048
}

/// <summary>
/// Texture import helpers for loading source images and creating GPU textures.
/// </summary>
public static class TextureImporter
{
    public sealed class ImportedTextureData
    {
        public required int Size { get; init; }
        public required PixelFormat PixelFormat { get; init; }
        public required IReadOnlyList<byte[]> MipLevels { get; init; }
    }

    /// <summary>
    /// Imports an image file, resizes it to the requested square size and uploads a full mip chain.
    /// </summary>
    public static Texture? ImportFromFile(
        string filePath,
        GraphicsDevice device,
        CommandList commandList,
        TextureSize targetSize = TextureSize.Size512,
        bool isNormalMap = false)
    {
        if (string.Equals(Path.GetExtension(filePath), ".dds", StringComparison.OrdinalIgnoreCase))
        {
            return ImportDdsTexture(filePath, device, isNormalMap);
        }

        var importedData = ImportTextureData(filePath, targetSize, isNormalMap);
        if (importedData == null)
            return null;

        return CreateTextureFromData(device, commandList, importedData);
    }

    /// <summary>
    /// Loads the image file and builds a full mip chain in CPU memory.
    /// </summary>
    public static ImportedTextureData? ImportTextureData(
        string filePath,
        TextureSize targetSize = TextureSize.Size512,
        bool isNormalMap = false)
    {
        try
        {
            int size = (int)targetSize;
            using var original = ImageSharpImage.Load<Rgba32>(filePath);
            using var resized = original.Clone(ctx => ctx.Resize(size, size));

            return new ImportedTextureData
            {
                Size = size,
                PixelFormat = isNormalMap ? PixelFormat.R8G8B8A8_UNorm : PixelFormat.R8G8B8A8_UNorm_SRgb,
                MipLevels = BuildMipChain(resized, size)
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a GPU texture from precomputed mip data.
    /// </summary>
    public static Texture CreateTextureFromData(GraphicsDevice device, CommandList commandList, ImportedTextureData textureData)
    {
        var texture = Texture.New2D(
            device,
            textureData.Size,
            textureData.Size,
            textureData.MipLevels.Count,
            textureData.PixelFormat,
            TextureFlags.ShaderResource);

        for (int mipLevel = 0; mipLevel < textureData.MipLevels.Count; mipLevel++)
        {
            texture.SetData(commandList, textureData.MipLevels[mipLevel], 0, mipLevel);
        }

        return texture;
    }

    /// <summary>
    /// Checks whether the file extension is a supported image format.
    /// </summary>
    public static bool IsSupportedImageFormat(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".dds" or ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".tiff" or ".tif";
    }

    /// <summary>
    /// Attempts to find a matching normal map next to the selected albedo map.
    /// </summary>
    public static string? FindMatchingNormalMap(string albedoPath)
    {
        if (!File.Exists(albedoPath))
            return null;

        string directory = Path.GetDirectoryName(albedoPath) ?? "";
        string nameWithoutExt = Path.GetFileNameWithoutExtension(albedoPath);

        string[] supportedExts = { ".dds", ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tiff", ".tif" };
        string[] diffuseSuffixes =
        {
            "_diffuse", "_Diffuse", "_DIFFUSE",
            "_albedo", "_Albedo", "_ALBEDO",
            "_basecolor", "_BaseColor", "_baseColor",
            "_color", "_Color", "_COLOR",
            "_d", "_D"
        };

        string[] normalSuffixes =
        {
            "_normal", "_Normal", "_NORMAL",
            "_n", "_N",
            "_normalmap", "_NormalMap", "_Normalmap"
        };

        foreach (var diffuseSuffix in diffuseSuffixes)
        {
            if (!nameWithoutExt.EndsWith(diffuseSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            string baseName = nameWithoutExt[..^diffuseSuffix.Length];
            foreach (var normalSuffix in normalSuffixes)
            {
                foreach (var ext in supportedExts)
                {
                    string candidate = Path.Combine(directory, baseName + normalSuffix + ext);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        foreach (var normalSuffix in normalSuffixes)
        {
            foreach (var ext in supportedExts)
            {
                string candidate = Path.Combine(directory, nameWithoutExt + normalSuffix + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        foreach (var subdir in new[] { Path.Combine(directory, "Normal"), Path.Combine(directory, "normal") })
        {
            if (!Directory.Exists(subdir))
                continue;

            foreach (var ext in supportedExts)
            {
                string candidate = Path.Combine(subdir, nameWithoutExt + ext);
                if (File.Exists(candidate))
                    return candidate;
            }

            foreach (var normalSuffix in normalSuffixes)
            {
                foreach (var ext in supportedExts)
                {
                    string candidate = Path.Combine(subdir, nameWithoutExt + normalSuffix + ext);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        return null;
    }

    private static List<byte[]> BuildMipChain(SixLabors.ImageSharp.Image<Rgba32> baseImage, int baseSize)
    {
        var mipLevels = new List<byte[]>();

        for (int mipSize = baseSize; mipSize >= 1; mipSize /= 2)
        {
            using var mipImage = mipSize == baseSize
                ? baseImage.Clone()
                : baseImage.Clone(ctx => ctx.Resize(mipSize, mipSize));
            mipLevels.Add(ExtractRgbaBytes(mipImage, mipSize));

            if (mipSize == 1)
                break;
        }

        return mipLevels;
    }

    private static byte[] ExtractRgbaBytes(SixLabors.ImageSharp.Image<Rgba32> image, int size)
    {
        var data = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var pixel = image[x, y];
                int index = (y * size + x) * 4;
                data[index + 0] = pixel.R;
                data[index + 1] = pixel.G;
                data[index + 2] = pixel.B;
                data[index + 3] = pixel.A;
            }
        }

        return data;
    }

    private static Texture? ImportDdsTexture(string filePath, GraphicsDevice device, bool isNormalMap)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return Texture.Load(
                device,
                stream,
                TextureFlags.ShaderResource,
                GraphicsResourceUsage.Immutable,
                loadAsSrgb: !isNormalMap);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
