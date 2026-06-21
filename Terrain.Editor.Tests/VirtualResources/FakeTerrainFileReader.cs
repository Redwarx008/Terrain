using Terrain;

namespace Terrain.Editor.Tests.VirtualResources;

internal sealed class FakeTerrainFileReader : ITerrainFileReader
{
    private readonly TerrainMinMaxErrorMap[] minMaxErrorMaps;
    private readonly ushort[] heightData;
    private readonly int width;
    private readonly int height;

    public FakeTerrainFileReader(int width = 4, int height = 4, ushort[]? heightData = null, int? tileSize = null)
    {
        var map = new TerrainMinMaxErrorMap(1, 1);
        map.Set(0, 0, 10.0f, 20.0f, 0.5f);
        minMaxErrorMaps = [map];
        this.width = width;
        this.height = height;
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
            TileSize = tileSize ?? width,
            Padding = 1,
            BytesPerPixel = sizeof(ushort),
            Mipmaps = 1,
        };
    }

    public bool IsDisposed { get; private set; }
    public int HeightPageReadCount { get; private set; }
    public int ReadAllHeightDataCount { get; private set; }

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
        ReadAllHeightDataCount++;
        return heightData;
    }

    public void ReadHeightPage(TerrainPageKey key, Span<byte> destination)
    {
        HeightPageReadCount++;

        int paddedTileSize = HeightmapHeader.TileSize + HeightmapHeader.Padding * 2;
        destination[..(paddedTileSize * paddedTileSize * sizeof(ushort))].Clear();
        for (int y = 0; y < HeightmapHeader.TileSize; y++)
        {
            int sourceY = Math.Clamp(key.PageY * (HeightmapHeader.TileSize - 1) + y, 0, height - 1);
            for (int x = 0; x < HeightmapHeader.TileSize; x++)
            {
                int sourceX = Math.Clamp(key.PageX * (HeightmapHeader.TileSize - 1) + x, 0, width - 1);
                ushort value = heightData[sourceY * width + sourceX];
                int destinationIndex = ((y + HeightmapHeader.Padding) * paddedTileSize + x + HeightmapHeader.Padding) * sizeof(ushort);
                BitConverter.TryWriteBytes(destination[destinationIndex..], value);
            }
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
