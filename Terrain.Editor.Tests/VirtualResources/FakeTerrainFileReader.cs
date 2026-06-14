using Terrain;

namespace Terrain.Editor.Tests.VirtualResources;

internal sealed class FakeTerrainFileReader : ITerrainFileReader
{
    private readonly TerrainMinMaxErrorMap[] minMaxErrorMaps;

    public FakeTerrainFileReader()
    {
        var map = new TerrainMinMaxErrorMap(1, 1);
        map.Set(0, 0, 10.0f, 20.0f, 0.5f);
        minMaxErrorMaps = [map];
    }

    public bool IsDisposed { get; private set; }

    public TerrainFileHeader Header { get; } = new()
    {
        LeafNodeSize = 32,
        Width = 4,
        Height = 4,
    };

    public TerrainVirtualTextureHeader HeightmapHeader { get; } = new()
    {
        Width = 4,
        Height = 4,
        TileSize = 4,
        Padding = 1,
        BytesPerPixel = sizeof(ushort),
        Mipmaps = 1,
    };

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
        return [0, 1, 2, 3];
    }

    public void ReadHeightPage(TerrainPageKey key, Span<byte> destination)
    {
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
