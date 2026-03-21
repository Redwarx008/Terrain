using System.Runtime.InteropServices;

namespace TerrainPreProcessor.Models;

/// <summary>
/// .terrain 文件头结构
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainFileHeader
{
    // 魔数 "TERR"
    public int Magic;

    // 文件格式版本
    public int Version;

    // 地形尺寸
    public int Width;
    public int Height;

    // 地形参数
    public int LeafNodeSize;
    public int TileSize;       // 2^n + 1，不包含 padding
    public int Padding;        // 额外边缘像素

    // 高度图 SVT 信息
    public int HeightMapMipLevels;

    // SplatMap 信息
    public int HasSplatMap;    // bool 作为 int 存储（0/1）
    public int SplatMapFormat; // VTFormat 枚举值
    public int SplatMapMipLevels;

    // 预留字段，用于未来扩展
    public int Reserved1;
    public int Reserved2;
    public int Reserved3;
    public int Reserved4;

    public const int MAGIC_VALUE = 0x52524554; // "TERR" in little-endian
    public const int CURRENT_VERSION = 1;

    public static TerrainFileHeader CreateDefault()
    {
        return new TerrainFileHeader
        {
            Magic = MAGIC_VALUE,
            Version = CURRENT_VERSION,
            Width = 0,
            Height = 0,
            LeafNodeSize = 16,
            TileSize = 129,
            Padding = 1,
            HeightMapMipLevels = 0,
            HasSplatMap = 0,
            SplatMapFormat = 0,
            SplatMapMipLevels = 0,
            Reserved1 = 0,
            Reserved2 = 0,
            Reserved3 = 0,
            Reserved4 = 0
        };
    }

    public readonly bool IsValid => Magic == MAGIC_VALUE;
}
