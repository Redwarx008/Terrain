using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TerrainPreProcessor.Models;

namespace TerrainPreProcessor.Services;

/// <summary>
/// 地形预处理核心服务
/// 整合 MinMaxErrorMap 生成、SVT 生成、.terrain 文件打包
/// </summary>
public class TerrainProcessor
{
    /// <summary>
    /// 高度图默认 Padding（用于跨页邻域采样）
    /// </summary>
    private const int HeightMapPadding = 2;

    /// <summary>
    /// SplatMap 默认 Padding
    /// </summary>
    private const int SplatMapPadding = 2;

    /// <summary>
    /// 处理地形数据并生成 .terrain 文件
    /// </summary>
    /// <param name="config">处理配置</param>
    /// <param name="progress">进度报告器</param>
    /// <returns>处理结果</returns>
    public static async Task<Result> ProcessAsync(ProcessingConfig config, 
        IProgress<(int current, int total, string message)>? progress = null)
    {
        return await Task.Run(() => Process(config, progress));
    }

    /// <summary>
    /// 处理地形数据并生成 .terrain 文件
    /// </summary>
    /// <param name="config">处理配置</param>
    /// <param name="progress">进度报告器</param>
    /// <returns>处理结果</returns>
    public static Result Process(ProcessingConfig config, 
        IProgress<(int current, int total, string message)>? progress = null)
    {
        // 验证配置
        if (!config.Validate(out string errorMessage))
            return Result.Failure(errorMessage);

        // 验证文件路径
        var fileValidation = FileValidator.ValidateConfig(config);
        if (fileValidation.IsFailure)
            return fileValidation;

        try
        {
            return ProcessInternal(config, progress);
        }
        catch (Exception ex)
        {
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// 内部处理实现
    /// </summary>
    private static Result ProcessInternal(ProcessingConfig config, 
        IProgress<(int current, int total, string message)>? progress)
    {
        string outputPath = string.IsNullOrWhiteSpace(config.OutputPath)
            ? config.GetDefaultOutputPath()
            : config.OutputPath;

        // 使用 using 确保资源释放
        using var heightMap = Image.Load<L16>(config.HeightMapPath!);

        // 计算 LOD 层级数
        int maxDimension = Math.Max(heightMap.Width, heightMap.Height);
        int lodLevelCount = CalculateLodLevels(maxDimension, config.LeafNodeSize);

        // 计算 SVT mipmap 层级数
        int svtMipLevels = CoordinateConsistentMipmap.CalculateMipLevels(
            heightMap.Width, heightMap.Height, config.TileSize);

        // 生成 MinMaxErrorMap
        progress?.Report((5, 100, "生成 MinMaxErrorMap..."));
        var minMaxErrorMapsResult = MinMaxErrorMap.Generate(
            config.HeightMapPath!,
            config.LeafNodeSize,
            lodLevelCount,
            progress: null);

        if (minMaxErrorMapsResult.IsFailure)
            return Result.Failure(minMaxErrorMapsResult.ErrorMessage);

        var minMaxErrorMaps = minMaxErrorMapsResult.Value!;

        // 加载 SplatMap（如果有）
        Image? splatMap = null;
        VTFormat splatMapFormat = VTFormat.Rgba32;
        int splatMapMipLevels = 0;

        if (!string.IsNullOrWhiteSpace(config.SplatMapPath))
        {
            progress?.Report((50, 100, "加载 SplatMap..."));
            var splatMapInfo = LoadSplatMap(config.SplatMapPath);
            splatMap = splatMapInfo.image;
            splatMapFormat = splatMapInfo.format;
            splatMapMipLevels = CoordinateConsistentMipmap.CalculateMipLevels(
                splatMap.Width, splatMap.Height, config.TileSize);
        }

        try
        {
            // 生成 .terrain 文件
            progress?.Report((55, 100, "生成 .terrain 文件..."));
            WriteTerrainFile(
                outputPath,
                heightMap,
                minMaxErrorMaps,
                config,
                svtMipLevels,
                splatMap,
                splatMapFormat,
                splatMapMipLevels,
                progress);

            progress?.Report((100, 100, "处理完成！"));
            return Result.Success();
        }
        finally
        {
            splatMap?.Dispose();
        }
    }

    /// <summary>
    /// 计算 LOD 层级数
    /// </summary>
    private static int CalculateLodLevels(int maxDimension, int leafNodeSize)
    {
        int levels = 0;
        int current = maxDimension - 1;  // 顶点数 = 尺寸 - 1

        while (current > leafNodeSize)
        {
            current = (current + 1) / 2;
            levels++;
        }

        return levels + 1;  // +1 包含最精细层级
    }

    /// <summary>
    /// 加载 SplatMap 并自动检测格式
    /// </summary>
    private static (Image image, VTFormat format) LoadSplatMap(string path)
    {
        var image = Image.Load(path);

        // 根据像素格式自动检测 VTFormat
        VTFormat format = image switch
        {
            Image<L8> => VTFormat.R8,
            Image<L16> => VTFormat.L16,
            Image<Rg32> => VTFormat.Rg32,
            _ => VTFormat.Rgba32
        };

        return (image, format);
    }

    /// <summary>
    /// 写入 .terrain 文件
    /// 数据顺序：TerrainFileHeader → MinMaxErrorMap Data → HeightMap SVT Data → SplatMap SVT Data
    /// </summary>
    private static void WriteTerrainFile(
        string outputPath,
        Image<L16> heightMap,
        MinMaxErrorMap[] minMaxErrorMaps,
        ProcessingConfig config,
        int heightMapMipLevels,
        Image? splatMap,
        VTFormat splatMapFormat,
        int splatMapMipLevels,
        IProgress<(int current, int total, string message)>? progress)
    {
        using var fs = new FileStream(outputPath, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        // 写入文件头（稍后更新某些字段）
        var header = new TerrainFileHeader
        {
            Magic = TerrainFileHeader.MAGIC_VALUE,
            Version = TerrainFileHeader.CURRENT_VERSION,
            Width = heightMap.Width,
            Height = heightMap.Height,
            LeafNodeSize = config.LeafNodeSize,
            TileSize = config.TileSize,
            Padding = HeightMapPadding,
            HeightMapMipLevels = heightMapMipLevels,
            HasSplatMap = splatMap != null ? 1 : 0,
            SplatMapFormat = (int)splatMapFormat,
            SplatMapMipLevels = splatMapMipLevels
        };

        WriteTerrainHeader(writer, header);

        // 写入 MinMaxErrorMap 数据
        progress?.Report((60, 100, "写入 MinMaxErrorMap 数据..."));
        writer.Write(minMaxErrorMaps.Length);
        foreach (var map in minMaxErrorMaps)
        {
            map.WriteTo(writer);
        }

        // 写入高度图 SVT 数据
        progress?.Report((70, 100, "写入高度图 SVT 数据..."));
        WriteHeightMapSVT(writer, heightMap, config.TileSize, heightMapMipLevels, config, progress);

        // 写入 SplatMap SVT 数据（如果有）
        if (splatMap != null)
        {
            progress?.Report((85, 100, "写入 SplatMap SVT 数据..."));
            WriteSplatMapSVT(writer, splatMap, splatMapFormat, config.TileSize, splatMapMipLevels, config, progress);
        }
    }

    /// <summary>
    /// 写入 TerrainFileHeader
    /// </summary>
    private static void WriteTerrainHeader(BinaryWriter writer, TerrainFileHeader header)
    {
        ReadOnlySpan<byte> headerBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref header, 1));
        writer.Write(headerBytes);
    }

    /// <summary>
    /// 写入高度图 SVT 数据（使用坐标一致性 mipmap）
    /// </summary>
    private static void WriteHeightMapSVT(
        BinaryWriter writer,
        Image<L16> sourceImage,
        int tileSize,
        int mipLevels,
        ProcessingConfig config,
        IProgress<(int current, int total, string message)>? progress)
    {
        int bytesPerPixel = 2; // L16 = 2 bytes

#if DEBUG
        // 调试输出目录
        string debugDir = GetDebugOutputDirectory(config);
#endif

        // 写入 VTHeader
        var vtHeader = new VTHeader
        {
            Width = sourceImage.Width,
            Height = sourceImage.Height,
            TileSize = tileSize,
            Padding = HeightMapPadding,
            BytesPerPixel = bytesPerPixel,
            Mipmaps = mipLevels
        };

        WriteVTHeader(writer, vtHeader);

        // 写入每个 mipmap 层级的 tiles
        Image<L16>? currentMip = sourceImage;
        for (int mip = 0; mip < mipLevels; mip++)
        {
#if DEBUG
            // 调试输出：保存当前 mipmap 层级
            MipmapDebugOutput.SaveMipmapLevel(currentMip, debugDir, "HeightMap", mip);
#endif

            WriteMipLevelTiles(writer, currentMip, tileSize, HeightMapPadding, progress);

            // 生成下一级 mipmap（坐标一致性版本）
            if (mip < mipLevels - 1)
            {
                var nextMip = CoordinateConsistentMipmap.GenerateNextMip(currentMip);
                if (mip > 0) currentMip.Dispose();
                currentMip = nextMip;
            }
        }

        // 清理最后一个 mipmap
        if (mipLevels > 1 && currentMip != sourceImage)
        {
            currentMip.Dispose();
        }
    }

    /// <summary>
    /// 写入 SplatMap SVT 数据（使用坐标一致性 mipmap）
    /// </summary>
    private static void WriteSplatMapSVT(
        BinaryWriter writer,
        Image splatMap,
        VTFormat format,
        int tileSize,
        int mipLevels,
        ProcessingConfig config,
        IProgress<(int current, int total, string message)>? progress)
    {
        int bytesPerPixel = format switch
        {
            VTFormat.R8 => 1,
            VTFormat.L16 => 2,
            VTFormat.Rg32 => 4,
            _ => 4  // Rgba32
        };

        // 写入 VTHeader
        var vtHeader = new VTHeader
        {
            Width = splatMap.Width,
            Height = splatMap.Height,
            TileSize = tileSize,
            Padding = SplatMapPadding,
            BytesPerPixel = bytesPerPixel,
            Mipmaps = mipLevels
        };

        WriteVTHeader(writer, vtHeader);

#if DEBUG
        // 调试输出目录
        string debugDir = GetDebugOutputDirectory(config);
#endif

        // 根据格式处理
        switch (format)
        {
            case VTFormat.R8:
                WriteSplatMapSVTGeneric(writer, (Image<L8>)splatMap, tileSize, mipLevels
#if DEBUG
                    , debugDir
#endif
                    , progress);
                break;
            case VTFormat.L16:
                WriteSplatMapSVTGeneric(writer, (Image<L16>)splatMap, tileSize, mipLevels
#if DEBUG
                    , debugDir
#endif
                    , progress);
                break;
            case VTFormat.Rg32:
                WriteSplatMapSVTGeneric(writer, (Image<Rg32>)splatMap, tileSize, mipLevels
#if DEBUG
                    , debugDir
#endif
                    , progress);
                break;
            default:
                WriteSplatMapSVTGeneric(writer, (Image<Rgba32>)splatMap, tileSize, mipLevels
#if DEBUG
                    , debugDir
#endif
                    , progress);
                break;
        }
    }

    /// <summary>
    /// 写入 SplatMap SVT 数据（泛型版本）
    /// </summary>
    private static void WriteSplatMapSVTGeneric<TPixel>(
        BinaryWriter writer,
        Image<TPixel> sourceImage,
        int tileSize,
        int mipLevels
#if DEBUG
        , string debugOutputDirectory
#endif
        , IProgress<(int current, int total, string message)>? progress)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel>? currentMip = sourceImage;
        for (int mip = 0; mip < mipLevels; mip++)
        {
#if DEBUG
            // 调试输出：保存当前 mipmap 层级
            MipmapDebugOutput.SaveMipmapLevel(currentMip, debugOutputDirectory, "SplatMap", mip);
#endif

            WriteMipLevelTiles(writer, currentMip, tileSize, SplatMapPadding, progress);

            if (mip < mipLevels - 1)
            {
                var nextMip = CoordinateConsistentMipmap.GenerateNextMip(currentMip);
                if (mip > 0) currentMip.Dispose();
                currentMip = nextMip;
            }
        }

        if (mipLevels > 1 && currentMip != sourceImage)
        {
            currentMip.Dispose();
        }
    }

    /// <summary>
    /// 获取调试输出目录（仅 DEBUG 模式使用）
    /// </summary>
#if DEBUG
    private static string GetDebugOutputDirectory(ProcessingConfig config)
    {
        var outputPath = string.IsNullOrWhiteSpace(config.OutputPath)
            ? config.GetDefaultOutputPath()
            : config.OutputPath;

        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        return Path.Combine(directory, "DebugMipmaps");
    }
#endif

    /// <summary>
    /// 写入 VTHeader
    /// </summary>
    private static void WriteVTHeader(BinaryWriter writer, VTHeader header)
    {
        ReadOnlySpan<byte> headerBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref header, 1));
        writer.Write(headerBytes);
    }

    /// <summary>
    /// 写入某个 mipmap 层级的所有 tiles
    /// </summary>
    private static void WriteMipLevelTiles<TPixel>(
        BinaryWriter writer,
        Image<TPixel> source,
        int tileSize,
        int padding,
        IProgress<(int current, int total, string message)>? progress)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int paddedTileSize = tileSize + padding * 2;
        int effectiveTileSpan = Math.Max(1, tileSize - 1);
        int nTilesX = (int)Math.Ceiling(Math.Max(1, source.Width - 1) / (float)effectiveTileSpan);
        int nTilesY = (int)Math.Ceiling(Math.Max(1, source.Height - 1) / (float)effectiveTileSpan);

        for (int ty = 0; ty < nTilesY; ty++)
        {
            for (int tx = 0; tx < nTilesX; tx++)
            {
                // Runtime sampling treats each page as covering TileSize - 1 terrain cells,
                // so adjacent pages must overlap by one source sample along their shared edge.
                int tileOriginX = tx * effectiveTileSpan;
                int tileOriginY = ty * effectiveTileSpan;

                // padded tile 的读取范围（自动裁边）
                int readX = tileOriginX - padding;
                int readY = tileOriginY - padding;

                // 读取一个 tile
                TPixel[] tilePixels = ReadTileToPixels(
                    source,
                    readX, readY,
                    paddedTileSize, paddedTileSize);

                // 写入 VT 文件
                WriteTilePixels(writer, tilePixels);
            }
        }
    }

    /// <summary>
    /// 读取 tile 像素数据（带边界 clamp）
    /// </summary>
    private static TPixel[] ReadTileToPixels<TPixel>(
        Image<TPixel> src,
        int readX, int readY,
        int w, int h)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var pixels = new TPixel[w * h];
        int srcW = src.Width;
        int srcH = src.Height;

        src.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                int srcY = Math.Clamp(readY + y, 0, srcH - 1);
                var srcRow = accessor.GetRowSpan(srcY);

                for (int x = 0; x < w; x++)
                {
                    int srcX = Math.Clamp(readX + x, 0, srcW - 1);
                    pixels[y * w + x] = srcRow[srcX];
                }
            }
        });

        return pixels;
    }

    /// <summary>
    /// 写入 tile 像素数据
    /// </summary>
    private static void WriteTilePixels<TPixel>(BinaryWriter writer, TPixel[] pixels)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ReadOnlySpan<byte> byteView = MemoryMarshal.AsBytes(pixels.AsSpan());
        writer.Write(byteView);
    }
}
