using System.IO;

namespace TerrainPreProcessor.Models;

/// <summary>
/// 地形预处理配置参数
/// </summary>
public class ProcessingConfig
{
    /// <summary>
    /// 叶子节点尺寸（地形 chunk 的基础尺寸）
    /// 可选值: 8, 16, 32, 64
    /// </summary>
    public int LeafNodeSize { get; set; } = 16;

    /// <summary>
    /// SVT Tile 尺寸（2^n + 1，不包含 padding）
    /// 可选值: 129, 257, 513
    /// </summary>
    public int TileSize { get; set; } = 129;

    /// <summary>
    /// 高度图文件路径（必填）
    /// </summary>
    public string? HeightMapPath { get; set; }

    /// <summary>
    /// SplatMap 文件路径（可选）
    /// </summary>
    public string? SplatMapPath { get; set; }

    /// <summary>
    /// 输出文件路径（可选，默认使用高度图原目录）
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// 验证配置参数
    /// </summary>
    public bool Validate(out string errorMessage)
    {
        errorMessage = string.Empty;

        // 验证 LeafNodeSize
        if (LeafNodeSize != 8 && LeafNodeSize != 16 && LeafNodeSize != 32 && LeafNodeSize != 64)
        {
            errorMessage = "LeafNodeSize 必须是 8, 16, 32 或 64";
            return false;
        }

        // 验证 TileSize (必须是 2^n + 1)
        if (TileSize != 129 && TileSize != 257 && TileSize != 513)
        {
            errorMessage = "TileSize 必须是 129, 257 或 513";
            return false;
        }

        // 验证高度图路径
        if (string.IsNullOrWhiteSpace(HeightMapPath) || !File.Exists(HeightMapPath))
        {
            errorMessage = "高度图文件不存在";
            return false;
        }

        // 验证 SplatMap 路径（如果提供了）
        if (!string.IsNullOrWhiteSpace(SplatMapPath) && !File.Exists(SplatMapPath))
        {
            errorMessage = "SplatMap 文件不存在";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 获取默认输出路径（高度图原目录）
    /// </summary>
    public string GetDefaultOutputPath()
    {
        if (string.IsNullOrWhiteSpace(HeightMapPath))
            return string.Empty;

        var directory = Path.GetDirectoryName(HeightMapPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(HeightMapPath);
        return Path.Combine(directory, $"{fileName}.terrain");
    }
}
