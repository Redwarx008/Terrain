using System.IO;
using System.Linq;
using TerrainPreProcessor.Models;

namespace TerrainPreProcessor.Services;

/// <summary>
/// 文件验证服务
/// </summary>
public static class FileValidator
{
    /// <summary>
    /// 支持的高度图扩展名
    /// </summary>
    private static readonly string[] ValidHeightMapExtensions = { ".png", ".raw", ".r16" };

    /// <summary>
    /// 支持的 SplatMap 扩展名
    /// </summary>
    private static readonly string[] ValidSplatMapExtensions = { ".png", ".tga", ".jpg", ".jpeg" };

    /// <summary>
    /// 最大文件大小 (1GB)
    /// </summary>
    private const long MaxFileSizeBytes = 1024L * 1024L * 1024L;

    /// <summary>
    /// 验证高度图文件
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>验证结果</returns>
    public static Result ValidateHeightMap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result.Failure("高度图路径不能为空");

        if (!File.Exists(path))
            return Result.Failure($"高度图文件不存在: {path}");

        var fileInfo = new FileInfo(path);

        if (fileInfo.Length == 0)
            return Result.Failure("高度图文件为空");

        if (fileInfo.Length > MaxFileSizeBytes)
            return Result.Failure($"高度图文件过大，最大支持 {MaxFileSizeBytes / (1024 * 1024)} MB");

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!ValidHeightMapExtensions.Contains(extension))
            return Result.Failure($"不支持的高度图格式: {extension}，支持的格式: {string.Join(", ", ValidHeightMapExtensions)}");

        return Result.Success();
    }

    /// <summary>
    /// 验证 SplatMap 文件
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>验证结果</returns>
    public static Result ValidateSplatMap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result.Success(); // SplatMap 是可选的

        if (!File.Exists(path))
            return Result.Failure($"SplatMap 文件不存在: {path}");

        var fileInfo = new FileInfo(path);

        if (fileInfo.Length == 0)
            return Result.Failure("SplatMap 文件为空");

        if (fileInfo.Length > MaxFileSizeBytes)
            return Result.Failure($"SplatMap 文件过大，最大支持 {MaxFileSizeBytes / (1024 * 1024)} MB");

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!ValidSplatMapExtensions.Contains(extension))
            return Result.Failure($"不支持的 SplatMap 格式: {extension}，支持的格式: {string.Join(", ", ValidSplatMapExtensions)}");

        return Result.Success();
    }

    /// <summary>
    /// 验证输出路径
    /// </summary>
    /// <param name="path">输出路径</param>
    /// <returns>验证结果</returns>
    public static Result ValidateOutputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result.Failure("输出路径不能为空");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            return Result.Failure($"输出目录不存在: {directory}");

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".terrain")
            return Result.Failure("输出文件扩展名必须为 .terrain");

        return Result.Success();
    }

    /// <summary>
    /// 验证处理配置中的所有文件路径
    /// </summary>
    /// <param name="config">处理配置</param>
    /// <returns>验证结果</returns>
    public static Result ValidateConfig(ProcessingConfig config)
    {
        var heightMapResult = ValidateHeightMap(config.HeightMapPath);
        if (heightMapResult.IsFailure)
            return heightMapResult;

        var splatMapResult = ValidateSplatMap(config.SplatMapPath);
        if (splatMapResult.IsFailure)
            return splatMapResult;

        var outputPathResult = ValidateOutputPath(
            string.IsNullOrWhiteSpace(config.OutputPath) 
                ? config.GetDefaultOutputPath() 
                : config.OutputPath);
        if (outputPathResult.IsFailure)
            return outputPathResult;

        return Result.Success();
    }
}
