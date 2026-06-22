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
    public int Magic;
    public int Version;
    public int Width;
    public int Height;
    public int LeafNodeSize;
    public int TileSize;
    public int Padding;
    public int HeightMapMipLevels;
    public int DetailMapFormat;
    public int DetailMapMipLevels;
    public int DetailMapResolutionRatio;

    public const int MAGIC_VALUE = 0x52524554;
    public const int CURRENT_VERSION = 8;

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
