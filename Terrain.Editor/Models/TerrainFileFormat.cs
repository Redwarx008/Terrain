#nullable enable

using System.Runtime.InteropServices;

namespace Terrain.Editor.Models;

/// <summary>
/// Supported virtual-texture payload formats written into .terrain files.
/// </summary>
public enum VTFormat : int
{
    Rgba32 = 0,
    L16 = 1,
    Rg32 = 2,
    R8 = 3,
}

/// <summary>
/// .terrain file header structure.
/// Binary layout must match the runtime reader exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainFileHeader
{
    public int Magic;              // "TERR" = 0x52524554
    public int Version;
    public int Width;
    public int Height;
    public int LeafNodeSize;
    public int TileSize;          // 2^n + 1, without padding
    public int Padding;           // HeightMap padding
    public int HeightMapMipLevels;
    public int SplatMapFormat;     // v6+: BiomeMask VTFormat enum value
    public int SplatMapMipLevels;  // v6+: BiomeMask mip count
    public int SplatMapResolutionRatio;  // 1 = same as heightmap, 2 = 1/2 res biome mask
    public int RiverMapFormat;     // v7+: optional RiverMask VTFormat enum value, 0 when absent
    public int RiverMapMipLevels;  // v7+: RiverMask mip count
    public int RiverMapResolutionRatio;  // v7+: 1 = same as heightmap, 2 = 1/2 res river mask

    public const int MAGIC_VALUE = 0x52524554;
    public const int CURRENT_VERSION = 7;

    public readonly bool IsValid => Magic == MAGIC_VALUE;
}

/// <summary>
/// Header for a virtual-texture payload block embedded in a .terrain file.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VTHeader
{
    public int Width;
    public int Height;
    public int TileSize;
    public int Padding;
    public int BytesPerPixel;
    public int Mipmaps;
}
