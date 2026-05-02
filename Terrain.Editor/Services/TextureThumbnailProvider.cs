#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Stride.TextureConverter;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace Terrain.Editor.Services;

internal static class TextureThumbnailProvider
{
    private static readonly Dictionary<string, TextureThumbnailCacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? LoadFromPath(string? texturePath)
    {
        return LoadFromPath(texturePath, out _);
    }

    public static Bitmap? LoadFromPath(string? texturePath, out string? error)
    {
        string? resolvedPath = ResolveTextureThumbnailPath(texturePath);
        if (resolvedPath == null)
        {
            error = string.IsNullOrWhiteSpace(texturePath)
                ? "Texture path is empty."
                : $"Texture file was not found: {texturePath}.";
            return null;
        }

        string fullPath = Path.GetFullPath(resolvedPath);
        DateTime lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);

        if (Cache.TryGetValue(fullPath, out var cached)
            && cached.LastWriteUtc == lastWriteUtc)
        {
            error = cached.Bitmap == null
                ? $"File thumbnail decode failed for cached path: {fullPath}."
                : null;
            return cached.Bitmap;
        }

        cached?.Bitmap?.Dispose();
        Bitmap? bitmap = TryCreateTextureThumbnail(fullPath, out error);
        Cache[fullPath] = new TextureThumbnailCacheEntry(lastWriteUtc, bitmap);
        return bitmap;
    }

    private static string? ResolveTextureThumbnailPath(string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        if (File.Exists(texturePath))
            return texturePath;

        if (Path.IsPathRooted(texturePath))
            return null;

        var projectManager = ProjectManager.Instance;
        if (projectManager.IsProjectOpen)
        {
            string projectRelative = Path.Combine(
                projectManager.ProjectPath,
                texturePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(projectRelative))
                return projectRelative;

            string materialsRelative = Path.Combine(
                projectManager.MaterialsPath,
                texturePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(materialsRelative))
                return materialsRelative;
        }

        return null;
    }

    private static Bitmap? TryCreateTextureThumbnail(string texturePath, out string? error)
    {
        Bitmap? textureToolBitmap = TryCreateTextureThumbnailWithTextureTool(texturePath, out string? textureToolError);
        if (textureToolBitmap != null)
        {
            error = null;
            return textureToolBitmap;
        }

        Bitmap? avaloniaBitmap = TryCreateTextureThumbnailWithAvalonia(texturePath, out string? avaloniaError);
        if (avaloniaBitmap != null)
        {
            error = null;
            return avaloniaBitmap;
        }

        Bitmap? strideBitmap = TryCreateTextureThumbnailWithStride(texturePath, out string? strideError);
        if (strideBitmap != null)
        {
            error = null;
            return strideBitmap;
        }

        try
        {
            using var image = ImageSharpImage.Load<Rgba32>(texturePath);
            image.Mutate(static context => context.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(128, 128),
                Mode = ResizeMode.Crop,
            }));

            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            stream.Position = 0;
            error = null;
            return new Bitmap(stream);
        }
        catch (Exception exception)
        {
            error = $"File thumbnail decode failed. TextureTool: {textureToolError ?? "not attempted"} Avalonia: {avaloniaError ?? "not attempted"} Stride: {strideError ?? "not attempted"} ImageSharp: {exception.Message}";
            return null;
        }
    }

    private static Bitmap? TryCreateTextureThumbnailWithAvalonia(string texturePath, out string? error)
    {
        try
        {
            using var stream = File.OpenRead(texturePath);
            error = null;
            return new Bitmap(stream);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static Bitmap? TryCreateTextureThumbnailWithTextureTool(string texturePath, out string? error)
    {
        try
        {
            using var textureTool = new TextureTool();
            using var texImage = textureTool.Load(texturePath, isSRgb: true);

            if (IsCompressedTextureFormat(texImage.Format))
            {
                textureTool.Decompress(texImage, isSRgb: IsSrgbTextureFormat(texImage.Format));
            }

            textureTool.Resize(texImage, 128, 128, Filter.Rescaling.Lanczos3);

            using var image = textureTool.ConvertToStrideImage(texImage);
            using var pngStream = new MemoryStream();
            image.Save(pngStream, Stride.Graphics.ImageFileType.Png);

            error = null;
            return CreateOpaqueBitmapFromPngStream(pngStream);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static Bitmap? TryCreateTextureThumbnailWithStride(string texturePath, out string? error)
    {
        try
        {
            using var fileStream = File.OpenRead(texturePath);
            using var image = Stride.Graphics.Image.Load(fileStream, loadAsSRGB: true);
            using var pngStream = new MemoryStream();
            image.Save(pngStream, Stride.Graphics.ImageFileType.Png);
            error = null;
            return CreateOpaqueBitmapFromPngStream(pngStream);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static Bitmap CreateOpaqueBitmapFromPngStream(MemoryStream pngStream)
    {
        pngStream.Position = 0;
        using var image = ImageSharpImage.Load<Rgba32>(pngStream);
        image.ProcessPixelRows(static accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x].A = byte.MaxValue;
                }
            }
        });

        using var opaqueStream = new MemoryStream();
        image.Save(opaqueStream, new PngEncoder());
        opaqueStream.Position = 0;
        return new Bitmap(opaqueStream);
    }

    private static bool IsCompressedTextureFormat(Stride.Graphics.PixelFormat format)
    {
        return format is Stride.Graphics.PixelFormat.BC1_Typeless
            or Stride.Graphics.PixelFormat.BC1_UNorm
            or Stride.Graphics.PixelFormat.BC1_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC2_Typeless
            or Stride.Graphics.PixelFormat.BC2_UNorm
            or Stride.Graphics.PixelFormat.BC2_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC3_Typeless
            or Stride.Graphics.PixelFormat.BC3_UNorm
            or Stride.Graphics.PixelFormat.BC3_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC4_Typeless
            or Stride.Graphics.PixelFormat.BC4_UNorm
            or Stride.Graphics.PixelFormat.BC4_SNorm
            or Stride.Graphics.PixelFormat.BC5_Typeless
            or Stride.Graphics.PixelFormat.BC5_UNorm
            or Stride.Graphics.PixelFormat.BC5_SNorm
            or Stride.Graphics.PixelFormat.BC6H_Typeless
            or Stride.Graphics.PixelFormat.BC6H_Uf16
            or Stride.Graphics.PixelFormat.BC6H_Sf16
            or Stride.Graphics.PixelFormat.BC7_Typeless
            or Stride.Graphics.PixelFormat.BC7_UNorm
            or Stride.Graphics.PixelFormat.BC7_UNorm_SRgb
            or Stride.Graphics.PixelFormat.ETC1
            or Stride.Graphics.PixelFormat.ETC2_RGB
            or Stride.Graphics.PixelFormat.ETC2_RGB_SRgb
            or Stride.Graphics.PixelFormat.ETC2_RGB_A1
            or Stride.Graphics.PixelFormat.ETC2_RGBA
            or Stride.Graphics.PixelFormat.ETC2_RGBA_SRgb
            or Stride.Graphics.PixelFormat.EAC_R11_Unsigned
            or Stride.Graphics.PixelFormat.EAC_R11_Signed
            or Stride.Graphics.PixelFormat.EAC_RG11_Unsigned
            or Stride.Graphics.PixelFormat.EAC_RG11_Signed;
    }

    private static bool IsSrgbTextureFormat(Stride.Graphics.PixelFormat format)
    {
        return format is Stride.Graphics.PixelFormat.R8G8B8A8_UNorm_SRgb
            or Stride.Graphics.PixelFormat.B8G8R8A8_UNorm_SRgb
            or Stride.Graphics.PixelFormat.B8G8R8X8_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC1_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC2_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC3_UNorm_SRgb
            or Stride.Graphics.PixelFormat.BC7_UNorm_SRgb
            or Stride.Graphics.PixelFormat.ETC2_RGB_SRgb
            or Stride.Graphics.PixelFormat.ETC2_RGBA_SRgb;
    }

    private sealed record TextureThumbnailCacheEntry(DateTime LastWriteUtc, Bitmap? Bitmap);
}
