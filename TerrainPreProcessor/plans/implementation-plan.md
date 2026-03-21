# 代码审查问题实施方案

本文档针对代码审查中发现的关键问题提供详细的实施方案。

---

## 1. 并行优化

### 问题位置
- [`MinMaxErrorMap.cs:74-92`](Models/MinMaxErrorMap.cs:74) - 基础层级初始化
- [`MinMaxErrorMap.cs:117-145`](Models/MinMaxErrorMap.cs:117) - Min/Max 聚合
- [`MinMaxErrorMap.cs:150-162`](Models/MinMaxErrorMap.cs:150) - 几何误差计算

### 当前代码问题
```csharp
Parallel.For(0, chunkY, yChunkIndex =>
{
    for (int xChunkIndex = 0; xChunkIndex < chunkX; ++xChunkIndex)
    {
        // 内部循环串行执行
    }
});
```

### 优化方案

#### 方案 A: 扁平化并行循环
```csharp
// 将二维循环扁平化为一维，提高并行度
int totalChunks = chunkX * chunkY;
var options = new ParallelOptions 
{ 
    MaxDegreeOfParallelism = Environment.ProcessorCount 
};

Parallel.For(0, totalChunks, options, index =>
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
```

#### 方案 B: 使用 Partitioner 提高负载均衡
```csharp
var partitioner = Partitioner.Create(0, totalChunks);
Parallel.ForEach(partitioner, options, (range, state) =>
{
    for (int i = range.Item1; i < range.Item2; i++)
    {
        // 处理逻辑
    }
});
```

---

## 2. 文件路径验证

### 问题位置
- [`MinMaxErrorMap.cs:41`](Models/MinMaxErrorMap.cs:41) - 高度图加载
- [`TerrainProcessor.cs:56`](Services/TerrainProcessor.cs:56) - 高度图加载
- [`TerrainProcessor.cs:82`](Services/TerrainProcessor.cs:82) - SplatMap 加载

### 实施方案

#### 创建文件验证帮助类
```csharp
// Services/FileValidator.cs
namespace TerrainPreProcessor.Services;

/// <summary>
/// 文件验证服务
/// </summary>
public static class FileValidator
{
    private static readonly string[] ValidHeightMapExtensions = { ".png", ".raw", ".r16" };
    private static readonly string[] ValidSplatMapExtensions = { ".png", ".tga", ".jpg", ".jpeg" };
    
    /// <summary>
    /// 最大文件大小 (1GB)
    /// </summary>
    private const long MaxFileSizeBytes = 1024L * 1024L * 1024L;
    
    /// <summary>
    /// 验证高度图文件
    /// </summary>
    public static ValidationResult ValidateHeightMap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ValidationResult.Failure("高度图路径不能为空");
        
        if (!File.Exists(path))
            return ValidationResult.Failure($"高度图文件不存在: {path}");
        
        var fileInfo = new FileInfo(path);
        
        if (fileInfo.Length == 0)
            return ValidationResult.Failure("高度图文件为空");
        
        if (fileInfo.Length > MaxFileSizeBytes)
            return ValidationResult.Failure($"高度图文件过大，最大支持 {MaxFileSizeBytes / (1024 * 1024)} MB");
        
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!ValidHeightMapExtensions.Contains(extension))
            return ValidationResult.Failure($"不支持的高度图格式: {extension}，支持的格式: {string.Join(", ", ValidHeightMapExtensions)}");
        
        return ValidationResult.Success();
    }
    
    /// <summary>
    /// 验证 SplatMap 文件
    /// </summary>
    public static ValidationResult ValidateSplatMap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ValidationResult.Success(); // SplatMap 是可选的
        
        if (!File.Exists(path))
            return ValidationResult.Failure($"SplatMap 文件不存在: {path}");
        
        var fileInfo = new FileInfo(path);
        
        if (fileInfo.Length > MaxFileSizeBytes)
            return ValidationResult.Failure($"SplatMap 文件过大");
        
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!ValidSplatMapExtensions.Contains(extension))
            return ValidationResult.Failure($"不支持的 SplatMap 格式: {extension}");
        
        return ValidationResult.Success();
    }
    
    /// <summary>
    /// 验证输出路径
    /// </summary>
    public static ValidationResult ValidateOutputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ValidationResult.Failure("输出路径不能为空");
        
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            return ValidationResult.Failure($"输出目录不存在: {directory}");
        
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".terrain")
            return ValidationResult.Failure("输出文件扩展名必须为 .terrain");
        
        return ValidationResult.Success();
    }
}

/// <summary>
/// 验证结果
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; }
    public string ErrorMessage { get; }
    
    private ValidationResult(bool isValid, string errorMessage = "")
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }
    
    public static ValidationResult Success() => new(true);
    public static ValidationResult Failure(string message) => new(false, message);
}
```

#### 修改 MinMaxErrorMap.Generate
```csharp
public static MinMaxErrorMap[] Generate(string inputPath, int baseChunkSize, int lodLevelCount, 
    IProgress<(int current, int total, string message)>? progress = null)
{
    // 验证输入
    var validation = FileValidator.ValidateHeightMap(inputPath);
    if (!validation.IsValid)
        throw new ArgumentException(validation.ErrorMessage);
    
    if (baseChunkSize <= 0 || (baseChunkSize & (baseChunkSize - 1)) != 0)
        throw new ArgumentException("baseChunkSize 必须是 2 的幂次方", nameof(baseChunkSize));
    
    if (lodLevelCount <= 0)
        throw new ArgumentException("lodLevelCount 必须大于 0", nameof(lodLevelCount));
    
    progress?.Report((0, 100, "加载高度图..."));
    
    // 使用 using 确保资源释放
    using Image<L16> heightMapImage = Image.Load<L16>(inputPath);
    // ... 其余代码
}
```

---

## 3. 二进制读取验证

### 问题位置
- [`MinMaxErrorMap.cs:270-284`](Models/MinMaxErrorMap.cs:270) - ReadFrom 方法

### 实施方案

```csharp
/// <summary>
/// 从二进制流读取
/// </summary>
public static MinMaxErrorMap ReadFrom(BinaryReader reader)
{
    const int MaxDimension = 65536; // 最大维度限制
    const int MaxTotalSize = MaxDimension * MaxDimension * 3 * sizeof(float); // 最大内存限制
    
    // 读取并验证尺寸
    int width = reader.ReadInt32();
    int height = reader.ReadInt32();
    
    if (width <= 0 || height <= 0)
        throw new InvalidDataException($"无效的尺寸: {width}x{height}，尺寸必须大于 0");
    
    if (width > MaxDimension || height > MaxDimension)
        throw new InvalidDataException($"尺寸超出限制: {width}x{height}，最大支持 {MaxDimension}x{MaxDimension}");
    
    // 计算并验证数据大小
    long expectedDataSize = (long)width * height * 3 * sizeof(float);
    if (expectedDataSize > MaxTotalSize)
        throw new InvalidDataException($"数据大小超出内存限制: {expectedDataSize / (1024 * 1024)} MB");
    
    var map = new MinMaxErrorMap(width, height);
    
    // 安全读取数据
    Span<byte> byteView = MemoryMarshal.AsBytes(map._data.AsSpan());
    int bytesRead = reader.Read(byteView);
    
    if (bytesRead != byteView.Length)
    {
        throw new EndOfStreamException(
            $"数据不完整: 期望 {byteView.Length} 字节，实际读取 {bytesRead} 字节");
    }
    
    // 验证数据有效性（抽样检查）
    ValidateData(map._data);
    
    return map;
}

/// <summary>
/// 验证数据有效性
/// </summary>
private static void ValidateData(float[] data)
{
    // 抽样检查 NaN 和 Infinity
    int sampleStep = Math.Max(1, data.Length / 1000);
    for (int i = 0; i < data.Length; i += sampleStep)
    {
        if (float.IsNaN(data[i]) || float.IsInfinity(data[i]))
            throw new InvalidDataException($"检测到无效数据值在索引 {i}: {data[i]}");
    }
}
```

---

## 4. 资源泄漏风险

### 问题位置
- [`TerrainProcessor.cs:56-104`](Services/TerrainProcessor.cs:56) - Process 方法

### 当前代码问题
```csharp
var heightMap = Image.Load<L16>(config.HeightMapPath!);
// ... 多个操作 ...
heightMap.Dispose();  // 如果中间发生异常，不会执行
splatMap?.Dispose();
```

### 实施方案

```csharp
public static void Process(ProcessingConfig config, IProgress<(int current, int total, string message)>? progress = null)
{
    if (!config.Validate(out string errorMessage))
    {
        throw new ArgumentException(errorMessage);
    }

    string outputPath = string.IsNullOrWhiteSpace(config.OutputPath)
        ? config.GetDefaultOutputPath()
        : config.OutputPath;

    // 使用 using 声明确保资源释放
    using var heightMap = Image.Load<L16>(config.HeightMapPath!);
    
    // 计算 LOD 层级数
    int maxDimension = Math.Max(heightMap.Width, heightMap.Height);
    int lodLevelCount = CalculateLodLevels(maxDimension, config.LeafNodeSize);

    // 计算 SVT mipmap 层级数
    int svtMipLevels = CoordinateConsistentMipmap.CalculateMipLevels(
        heightMap.Width, heightMap.Height, config.TileSize);

    // 生成 MinMaxErrorMap
    progress?.Report((5, 100, "生成 MinMaxErrorMap..."));
    var minMaxErrorMaps = MinMaxErrorMap.Generate(
        config.HeightMapPath!,
        config.LeafNodeSize,
        lodLevelCount,
        progress: null);

    // 加载 SplatMap（如果有）- 使用 using 声明
    using Image? splatMap = !string.IsNullOrWhiteSpace(config.SplatMapPath)
        ? LoadSplatMap(config.SplatMapPath).image
        : null;
    
    VTFormat splatMapFormat = VTFormat.Rgba32;
    int splatMapMipLevels = 0;
    
    if (splatMap != null)
    {
        progress?.Report((50, 100, "加载 SplatMap..."));
        var splatMapInfo = LoadSplatMap(config.SplatMapPath!);
        splatMapFormat = splatMapInfo.format;
        splatMapMipLevels = CoordinateConsistentMipmap.CalculateMipLevels(
            splatMap.Width, splatMap.Height, config.TileSize);
    }

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
    // heightMap 和 splatMap 会在方法结束时自动 Dispose
}
```

---

## 5. 静态字段线程安全

### 问题位置
- [`TerrainProcessor.cs:30`](Services/TerrainProcessor.cs:30) - `_processedTilesCount`

### 当前代码问题
```csharp
private static int _processedTilesCount = 0;
// ...
_processedTilesCount++;  // 非原子操作，线程不安全
```

### 实施方案

#### 方案 A: 移除静态字段（推荐）
```csharp
// 删除静态字段
// private static int _processedTilesCount = 0;

private static void WriteMipLevelTiles<TPixel>(
    BinaryWriter writer,
    Image<TPixel> source,
    int tileSize,
    int padding,
    IProgress<(int current, int total, string message)>? progress)
    where TPixel : unmanaged, IPixel<TPixel>
{
    int paddedTileSize = tileSize + padding * 2;
    int nTilesX = (int)Math.Ceiling(source.Width / (float)tileSize);
    int nTilesY = (int)Math.Ceiling(source.Height / (float)tileSize);
    int totalTiles = nTilesX * nTilesY;
    
    // 使用局部变量
    int processedTiles = 0;
    
    for (int ty = 0; ty < nTilesY; ty++)
    {
        for (int tx = 0; tx < nTilesX; tx++)
        {
            // ... 处理逻辑 ...
            
            processedTiles++;
            
            // 可选：报告进度
            if (progress != null && processedTiles % 10 == 0)
            {
                progress.Report((processedTiles, totalTiles, $"处理 tile {processedTiles}/{totalTiles}"));
            }
        }
    }
}
```

#### 方案 B: 使用 Interlocked（如果确实需要静态计数）
```csharp
private static int _processedTilesCount = 0;

// 使用原子操作
Interlocked.Increment(ref _processedTilesCount);

// 重置时也需要原子操作
Interlocked.Exchange(ref _processedTilesCount, 0);
```

---

## 6. 引入 Result 模式

### 创建 Result 类型
```csharp
// Models/Result.cs
namespace TerrainPreProcessor.Models;

/// <summary>
/// 操作结果（无返回值）
/// </summary>
public readonly struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string ErrorMessage { get; }
    public Exception? Exception { get; }
    
    private Result(bool isSuccess, string errorMessage, Exception? exception)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Exception = exception;
    }
    
    public static Result Success() => new(true, string.Empty, null);
    public static Result Failure(string message) => new(false, message, null);
    public static Result Failure(Exception ex) => new(false, ex.Message, ex);
    
    public void ThrowIfFailure()
    {
        if (IsFailure)
            throw Exception ?? new InvalidOperationException(ErrorMessage);
    }
}

/// <summary>
/// 操作结果（带返回值）
/// </summary>
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string ErrorMessage { get; }
    public Exception? Exception { get; }
    
    private Result(bool isSuccess, T? value, string errorMessage, Exception? exception)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        Exception = exception;
    }
    
    public static Result<T> Success(T value) => new(true, value, string.Empty, null);
    public static Result<T> Failure(string message) => new(false, default, message, null);
    public static Result<T> Failure(Exception ex) => new(false, default, ex.Message, ex);
    
    public T GetValueOrThrow()
    {
        if (IsFailure)
            throw Exception ?? new InvalidOperationException(ErrorMessage);
        return Value!;
    }
    
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (IsFailure)
            return Result<TNew>.Failure(ErrorMessage);
        return Result<TNew>.Success(mapper(Value!));
    }
}
```

### 应用到 MinMaxErrorMap
```csharp
/// <summary>
/// 生成 MinMaxErrorMap 数组
/// </summary>
public static Result<MinMaxErrorMap[]> Generate(string inputPath, int baseChunkSize, int lodLevelCount, 
    IProgress<(int current, int total, string message)>? progress = null)
{
    // 验证输入
    var pathValidation = FileValidator.ValidateHeightMap(inputPath);
    if (pathValidation.IsFailure)
        return Result<MinMaxErrorMap[]>.Failure(pathValidation.ErrorMessage);
    
    if (baseChunkSize <= 0 || (baseChunkSize & (baseChunkSize - 1)) != 0)
        return Result<MinMaxErrorMap[]>.Failure("baseChunkSize 必须是 2 的幂次方");
    
    if (lodLevelCount <= 0)
        return Result<MinMaxErrorMap[]>.Failure("lodLevelCount 必须大于 0");
    
    try
    {
        progress?.Report((0, 100, "加载高度图..."));
        
        using Image<L16> heightMapImage = Image.Load<L16>(inputPath);
        int mapWidth = heightMapImage.Width;
        int mapHeight = heightMapImage.Height;

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

        // ... 其余处理逻辑 ...

        progress?.Report((100, 100, "MinMaxErrorMap 生成完成"));
        return Result<MinMaxErrorMap[]>.Success(maps);
    }
    catch (Exception ex)
    {
        return Result<MinMaxErrorMap[]>.Failure(ex);
    }
}
```

### 应用到 TerrainProcessor
```csharp
/// <summary>
/// 处理地形数据并生成 .terrain 文件
/// </summary>
public static async Task<Result> ProcessAsync(ProcessingConfig config, 
    IProgress<(int current, int total, string message)>? progress = null)
{
    return await Task.Run(() => Process(config, progress));
}

/// <summary>
/// 处理地形数据并生成 .terrain 文件
/// </summary>
public static Result Process(ProcessingConfig config, 
    IProgress<(int current, int total, string message)>? progress = null)
{
    // 验证配置
    if (!config.Validate(out string errorMessage))
        return Result.Failure(errorMessage);

    try
    {
        string outputPath = string.IsNullOrWhiteSpace(config.OutputPath)
            ? config.GetDefaultOutputPath()
            : config.OutputPath;

        using var heightMap = Image.Load<L16>(config.HeightMapPath!);
        
        // ... 处理逻辑 ...

        return Result.Success();
    }
    catch (Exception ex)
    {
        return Result.Failure(ex);
    }
}
```

### 应用到 ViewModel
```csharp
[RelayCommand]
private async Task StartProcessing()
{
    if (IsProcessing) return;

    var config = new ProcessingConfig
    {
        HeightMapPath = HeightMapPath,
        SplatMapPath = SplatMapPath,
        OutputPath = OutputPath,
        LeafNodeSize = LeafNodeSize,
        TileSize = TileSize
    };

    IsProcessing = true;
    StatusMessage = Strings.Processing;

    try
    {
        var progressWindow = _windowService.ShowProgressWindow(_loc["ProcessingTerrainData"]);

        IProgress<(int current, int total, string message)> progressReporter = 
            new Progress<(int current, int total, string message)>(progress =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    progressWindow.UpdateProgress(progress.current, progress.total, progress.message);
                });
            });

        // 使用 Result 模式
        var result = await TerrainProcessor.ProcessAsync(config, progressReporter);

        progressWindow.Close();

        if (result.IsSuccess)
        {
            StatusMessage = Strings.ProcessingComplete;
            await _windowService.ShowInfoDialogAsync(_loc["Complete"], _loc["TerrainProcessingComplete"]);
        }
        else
        {
            StatusMessage = $"Error: {result.ErrorMessage}";
            await _windowService.ShowErrorDialogAsync(_loc["ProcessingError"], result.ErrorMessage);
        }
    }
    catch (Exception ex)
    {
        StatusMessage = $"Error: {ex.Message}";
        await _windowService.ShowErrorDialogAsync(_loc["ProcessingError"], ex.Message);
    }
    finally
    {
        IsProcessing = false;
    }
}
```

---

## 实施优先级

| 优先级 | 问题 | 复杂度 | 影响范围 |
|--------|------|--------|----------|
| 1 | 资源泄漏风险 | 低 | TerrainProcessor.cs |
| 2 | 静态字段线程安全 | 低 | TerrainProcessor.cs |
| 3 | 文件路径验证 | 中 | 新增 FileValidator.cs |
| 4 | 二进制读取验证 | 中 | MinMaxErrorMap.cs |
| 5 | 并行优化 | 中 | MinMaxErrorMap.cs |
| 6 | 引入 Result 模式 | 高 | 多个文件 |

---

## 文件修改清单

1. **新增文件**
   - `Services/FileValidator.cs` - 文件验证服务
   - `Models/Result.cs` - Result 类型定义

2. **修改文件**
   - `Models/MinMaxErrorMap.cs` - 并行优化、输入验证、二进制读取验证
   - `Services/TerrainProcessor.cs` - 资源管理、静态字段移除、Result 模式
   - `ViewModels/MainWindowViewModel.cs` - Result 模式适配
