using System.Runtime.InteropServices;

namespace TerrainPreProcessor.Models;

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
