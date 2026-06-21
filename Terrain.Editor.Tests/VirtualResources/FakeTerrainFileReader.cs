using Terrain;

namespace Terrain.Editor.Tests.VirtualResources;

internal sealed class FakeTerrainFileReader : ITerrainFileReader
{
    private readonly TerrainMinMaxErrorMap[] minMaxErrorMaps;
    private readonly ushort[] heightData;

    public FakeTerrainFileReader(int width = 4, int height = 4, ushort[]? heightData = null)
    {
        var map = new TerrainMinMaxErrorMap(1, 1);
        map.Set(0, 0, 10.0f, 20.0f, 0.5f);
        minMaxErrorMaps = [map];
        this.heightData = heightData ?? new ushort[width * height];
        Header = new TerrainFileHeader
        {
            LeafNodeSize = 32,
            Width = width,
            Height = height,
        };
        HeightmapHeader = new TerrainVirtualTextureHeader
        {
            Width = width,
            Height = height,
            TileSize = width,
            Padding = 1,
            BytesPerPixel = sizeof(ushort),
            Mipmaps = 1,
        };
    }

    public bool IsDisposed { get; private set; }

    public TerrainFileHeader Header { get; }

    public TerrainVirtualTextureHeader HeightmapHeader { get; }

    public TerrainVirtualTextureHeader SplatMapHeader { get; } = new()
    {
        Width = 2,
        Height = 2,
        TileSize = 2,
        Padding = 1,
        BytesPerPixel = 1,
        Mipmaps = 1,
    };

    public int SplatMapResolutionRatio => 2;

    public int SplatMapMipCount => 1;

    public TerrainMinMaxErrorMap[] ReadAllMinMaxErrorMaps()
    {
        return minMaxErrorMaps;
    }

    public ushort[] ReadAllHeightData()
    {
        return heightData;
    }

    public void ReadHeightPage(TerrainPageKey key, Span<byte> destination)
    {
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
