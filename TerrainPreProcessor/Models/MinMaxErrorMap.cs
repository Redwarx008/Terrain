using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TerrainPreProcessor.Services;

namespace TerrainPreProcessor.Models;

/// <summary>
/// 几何误差地图（用于地形 LOD 选择）
/// 数据格式: [min, max, error] 交替存储
/// 数值范围: 0.0 - 65535.0 (对应 L16)
/// </summary>
public class MinMaxErrorMap
{
    private float[] _data;
    
    /// <summary>
    /// 地图宽度（chunk 数量）
    /// </summary>
    public int Width { get; }
    
    /// <summary>
    /// 地图高度（chunk 数量）
    /// </summary>
    public int Height { get; }
    
    /// <summary>
    /// 获取数据只读视图
    /// </summary>
    public ReadOnlySpan<float> Data => _data.AsSpan();

    private MinMaxErrorMap(int dimX, int dimY)
    {
        Width = dimX;
        Height = dimY;
        _data = new float[dimX * dimY * 3];
    }

    /// <summary>
    /// 生成 MinMaxErrorMap 数组
    /// </summary>
    /// <param name="inputPath">高度图文件路径</param>
    /// <param name="baseChunkSize">基础 chunk 尺寸（必须是 2 的幂次方）</param>
    /// <param name="lodLevelCount">LOD 层级数（必须大于 0）</param>
    /// <param name="progress">进度报告器</param>
    /// <returns>MinMaxErrorMap 数组，索引 0 为最精细层级</returns>
    public static Result<MinMaxErrorMap[]> Generate(string inputPath, int baseChunkSize, int lodLevelCount, 
        IProgress<(int current, int total, string message)>? progress = null)
    {
        // 验证输入文件
        var fileValidation = FileValidator.ValidateHeightMap(inputPath);
        if (fileValidation.IsFailure)
            return Result<MinMaxErrorMap[]>.Failure(fileValidation.ErrorMessage);

        // 验证参数
        if (baseChunkSize <= 0 || (baseChunkSize & (baseChunkSize - 1)) != 0)
            return Result<MinMaxErrorMap[]>.Failure("baseChunkSize 必须是 2 的幂次方");

        if (lodLevelCount <= 0)
            return Result<MinMaxErrorMap[]>.Failure("lodLevelCount 必须大于 0");

        try
        {
            return GenerateInternal(inputPath, baseChunkSize, lodLevelCount, progress);
        }
        catch (Exception ex)
        {
            return Result<MinMaxErrorMap[]>.Failure(ex);
        }
    }

    /// <summary>
    /// 内部生成实现
    /// </summary>
    private static Result<MinMaxErrorMap[]> GenerateInternal(string inputPath, int baseChunkSize, int lodLevelCount,
        IProgress<(int current, int total, string message)>? progress)
    {
        progress?.Report((0, 100, "加载高度图..."));

        using Image<L16> heightMapImage = Image.Load<L16>(inputPath);
        int mapWidth = heightMapImage.Width;
        int mapHeight = heightMapImage.Height;

        // 将图像数据转为 float 数组 (0.0 - 65535.0)
        float[] rawHeights = new float[mapWidth * mapHeight];

        heightMapImage.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; ++y)
            {
                var pixelRow = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    rawHeights[x + y * mapWidth] = (float)pixelRow[x].PackedValue;
                }
            }
        });

        progress?.Report((10, 100, "初始化基础层级..."));

        // 初始化基础层级
        int baseDimX = (int)Math.Ceiling(mapWidth / (float)baseChunkSize);
        int baseDimY = (int)Math.Ceiling(mapHeight / (float)baseChunkSize);

        var maps = new MinMaxErrorMap[lodLevelCount];
        maps[0] = new MinMaxErrorMap(baseDimX, baseDimY);

        // 初始化基础层级的 MinMaxError（优化并行）
        int chunkVertices = baseChunkSize + 1;
        int chunkY = (int)Math.Ceiling(mapHeight / (float)baseChunkSize);
        int chunkX = (int)Math.Ceiling(mapWidth / (float)baseChunkSize);
        int totalChunks = chunkX * chunkY;

        // 使用扁平化并行循环提高并行度
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        Parallel.For(0, totalChunks, parallelOptions, index =>
        {
            int yChunkIndex = index / chunkX;
            int xChunkIndex = index % chunkX;

            int y = yChunkIndex * baseChunkSize;
            int x = xChunkIndex * baseChunkSize;
            int sizeX = Math.Min(chunkVertices, mapWidth - x);
            int sizeY = Math.Min(chunkVertices, mapHeight - y);

            GetAreaMinMaxHeight(rawHeights, mapWidth, mapHeight, x, y, sizeX, sizeY, out float minHeight, out float maxHeight);
            float error = CalculateGeometricError(rawHeights, mapWidth, mapHeight, x, y, sizeX, sizeY, 0);

            int dataIndex = index * 3;
            maps[0]._data[dataIndex] = minHeight;
            maps[0]._data[dataIndex + 1] = maxHeight;
            maps[0]._data[dataIndex + 2] = error;
        });

        // 递归生成更高 LOD 级别
        for (int i = 1; i < lodLevelCount; ++i)
        {
            int progressPercent = 10 + (i * 80 / lodLevelCount);
            progress?.Report((progressPercent, 100, $"生成 LOD 层级 {i}/{lodLevelCount - 1}..."));
            maps[i] = CreateFromHigherDetail(rawHeights, mapWidth, mapHeight, maps[i - 1], i, baseChunkSize, parallelOptions);
        }

        progress?.Report((100, 100, "MinMaxErrorMap 生成完成"));
        return Result<MinMaxErrorMap[]>.Success(maps);
    }

    private static MinMaxErrorMap CreateFromHigherDetail(float[] rawHeights, int mapWidth, int mapHeight,
        MinMaxErrorMap higherDetail, int lodLevel, int baseChunkSize, ParallelOptions parallelOptions)
    {
        int srcDimX = higherDetail.Width;
        int srcDimY = higherDetail.Height;

        int dimX = (srcDimX + 1) >> 1;
        int dimY = (srcDimY + 1) >> 1;

        var dst = new MinMaxErrorMap(dimX, dimY);
        int totalDst = dimX * dimY;

        // Min/Max 聚合（优化并行 - 扁平化）
        Parallel.For(0, totalDst, parallelOptions, index =>
        {
            int dstY = index / dimX;
            int dstX = index % dimX;

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int dy = 0; dy < 2; ++dy)
            {
                for (int dx = 0; dx < 2; ++dx)
                {
                    int srcX = (dstX << 1) + dx;
                    int srcY = (dstY << 1) + dy;

                    if (srcX < srcDimX && srcY < srcDimY)
                    {
                        int srcIndex = srcX + srcY * srcDimX;
                        min = MathF.Min(min, higherDetail._data[srcIndex * 3]);
                        max = MathF.Max(max, higherDetail._data[srcIndex * 3 + 1]);
                    }
                }
            }

            int dataIndex = index * 3;
            dst._data[dataIndex] = min;
            dst._data[dataIndex + 1] = max;
            dst._data[dataIndex + 2] = 0;
        });

        int chunkSize = baseChunkSize << lodLevel;

        // 计算几何误差（优化并行 - 扁平化）
        Parallel.For(0, totalDst, parallelOptions, index =>
        {
            int y = index / dimX;
            int x = index % dimX;

            int startX = x * chunkSize;
            int startY = y * chunkSize;
            int sizeX = Math.Max(1, Math.Min(chunkSize + 1, mapWidth - startX));
            int sizeY = Math.Max(1, Math.Min(chunkSize + 1, mapHeight - startY));

            float error = CalculateGeometricError(rawHeights, mapWidth, mapHeight, startX, startY, sizeX, sizeY, lodLevel);
            dst._data[(x + y * dimX) * 3 + 2] = error;
        });

        return dst;
    }

    private static void GetAreaMinMaxHeight(float[] rawHeights, int mapW, int mapH, int startX, int startY, int sizeX, int sizeY,
        out float minHeight, out float maxHeight)
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        int endX = startX + sizeX - 1;
        int endY = startY + sizeY - 1;

        // 优化：检查是否需要边界 clamp
        bool needsClamp = startX < 0 || startY < 0 || endX >= mapW || endY >= mapH;

        if (!needsClamp)
        {
            // 快速路径：无需 clamp
            for (int y = startY; y <= endY; y++)
            {
                int rowBase = y * mapW;
                for (int x = startX; x <= endX; x++)
                {
                    float height = rawHeights[x + rowBase];
                    if (height < min) min = height;
                    if (height > max) max = height;
                }
            }
        }
        else
        {
            // 慢速路径：需要 clamp
            for (int y = startY; y <= endY; y++)
            {
                int clampedY = Math.Clamp(y, 0, mapH - 1);
                int rowBase = clampedY * mapW;
                for (int x = startX; x <= endX; x++)
                {
                    int clampedX = Math.Clamp(x, 0, mapW - 1);
                    float height = rawHeights[clampedX + rowBase];
                    if (height < min) min = height;
                    if (height > max) max = height;
                }
            }
        }

        if (min == float.MaxValue) min = 0f;
        if (max == float.MinValue) max = 0f;

        minHeight = min;
        maxHeight = max;
    }

    private static float CalculateGeometricError(float[] rawHeights, int mapW, int mapH, int startX, int startY, int sizeX, int sizeY, int lod)
    {
        Debug.Assert(mapW * mapH == rawHeights.Length);

        float maxError = 0f;
        int stride = 1 << lod;
        int halfStride = stride / 2;

        float GetHeight(int x, int y)
        {
            int cx = Math.Clamp(x, 0, mapW - 1);
            int cy = Math.Clamp(y, 0, mapH - 1);
            return rawHeights[cx + cy * mapW];
        }

        // 水平方向误差
        for (int y = startY; y < startY + sizeY; y += stride)
        {
            for (int x = startX + halfStride; x < startX + sizeX - halfStride; x += stride)
            {
                float height = GetHeight(x, y);
                float left = GetHeight(x - halfStride, y);
                float right = GetHeight(x + halfStride, y);
                float simplifiedHeight = (left + right) / 2;
                float error = MathF.Abs(simplifiedHeight - height);
                maxError = MathF.Max(maxError, error);
            }
        }

        // 垂直方向误差
        for (int y = startY + halfStride; y < startY + sizeY - halfStride; y += stride)
        {
            for (int x = startX; x < startX + sizeX; x += stride)
            {
                float height = GetHeight(x, y);
                float up = GetHeight(x, y + halfStride);
                float down = GetHeight(x, y - halfStride);
                float simplifiedHeight = (up + down) / 2;
                float error = MathF.Abs(simplifiedHeight - height);
                maxError = MathF.Max(maxError, error);
            }
        }

        // 对角方向误差
        for (int y = startY + halfStride; y < startY + sizeY - halfStride; y += stride)
        {
            for (int x = startX + halfStride; x < startX + sizeX - halfStride; x += stride)
            {
                float height = GetHeight(x, y);
                float upLeft = GetHeight(x - halfStride, y + halfStride);
                float downRight = GetHeight(x + halfStride, y - halfStride);
                float simplifiedHeight = (upLeft + downRight) / 2;
                float error = MathF.Abs(simplifiedHeight - height);
                maxError = MathF.Max(maxError, error);
            }
        }

        return maxError;
    }

    /// <summary>
    /// 写入到二进制流
    /// </summary>
    public void WriteTo(BinaryWriter writer)
    {
        writer.Write(Width);
        writer.Write(Height);
        ReadOnlySpan<byte> byteView = MemoryMarshal.AsBytes(_data.AsSpan());
        writer.Write(byteView);
    }

    /// <summary>
    /// 从二进制流读取
    /// </summary>
    public static Result<MinMaxErrorMap> ReadFrom(BinaryReader reader)
    {
        const int MaxDimension = 65536;
        const long MaxTotalSize = (long)MaxDimension * MaxDimension * 3L * sizeof(float);

        try
        {
            // 读取并验证尺寸
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();

            if (width <= 0 || height <= 0)
                return Result<MinMaxErrorMap>.Failure($"无效的尺寸: {width}x{height}，尺寸必须大于 0");

            if (width > MaxDimension || height > MaxDimension)
                return Result<MinMaxErrorMap>.Failure($"尺寸超出限制: {width}x{height}，最大支持 {MaxDimension}x{MaxDimension}");

            // 计算并验证数据大小
            long expectedDataSize = (long)width * height * 3 * sizeof(float);
            if (expectedDataSize > MaxTotalSize)
                return Result<MinMaxErrorMap>.Failure($"数据大小超出内存限制: {expectedDataSize / (1024 * 1024)} MB");

            var map = new MinMaxErrorMap(width, height);

            // 安全读取数据
            Span<byte> byteView = MemoryMarshal.AsBytes(map._data.AsSpan());
            int bytesRead = reader.Read(byteView);

            if (bytesRead != byteView.Length)
            {
                return Result<MinMaxErrorMap>.Failure(
                    $"数据不完整: 期望 {byteView.Length} 字节，实际读取 {bytesRead} 字节");
            }

            // 验证数据有效性（抽样检查）
            var dataValidation = ValidateData(map._data);
            if (dataValidation.IsFailure)
                return Result<MinMaxErrorMap>.Failure(dataValidation.ErrorMessage);

            return Result<MinMaxErrorMap>.Success(map);
        }
        catch (EndOfStreamException ex)
        {
            return Result<MinMaxErrorMap>.Failure($"流数据不完整: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<MinMaxErrorMap>.Failure($"读取错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证数据有效性
    /// </summary>
    private static Result ValidateData(float[] data)
    {
        // 抽样检查 NaN 和 Infinity
        int sampleStep = Math.Max(1, data.Length / 1000);
        for (int i = 0; i < data.Length; i += sampleStep)
        {
            if (float.IsNaN(data[i]) || float.IsInfinity(data[i]))
                return Result.Failure($"检测到无效数据值在索引 {i}: {data[i]}");
        }
        return Result.Success();
    }
}
