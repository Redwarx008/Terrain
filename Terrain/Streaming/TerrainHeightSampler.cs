#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Terrain;

internal sealed class TerrainHeightSampler
{
    public const int MaxCachedPages = 4;

    private readonly ITerrainFileReader fileReader;
    private readonly Dictionary<TerrainPageKey, CachedPage> pages = new();
    private readonly LinkedList<TerrainPageKey> lru = new();
    private readonly object gate = new();
    private readonly int paddedTileSize;
    private readonly int pageByteSize;
    private readonly int pageSpan;
    private readonly VirtualTextureMipLayoutInfo mip0Layout;

    public TerrainHeightSampler(ITerrainFileReader fileReader)
    {
        this.fileReader = fileReader ?? throw new ArgumentNullException(nameof(fileReader));
        paddedTileSize = checked(fileReader.HeightmapHeader.TileSize + fileReader.HeightmapHeader.Padding * 2);
        pageByteSize = checked(paddedTileSize * paddedTileSize * fileReader.HeightmapHeader.BytesPerPixel);
        pageSpan = Math.Max(1, fileReader.HeightmapHeader.TileSize - 1);
        mip0Layout = VirtualTextureLayout.GetMipLayout(
            fileReader.HeightmapHeader.Width,
            fileReader.HeightmapHeader.Height,
            fileReader.HeightmapHeader.TileSize,
            mipLevel: 0);
    }

    public float GetHeight(int sampleX, int sampleZ, float heightScale)
    {
        sampleX = Math.Clamp(sampleX, 0, fileReader.HeightmapHeader.Width - 1);
        sampleZ = Math.Clamp(sampleZ, 0, fileReader.HeightmapHeader.Height - 1);

        int pageX = Math.Min(sampleX / pageSpan, mip0Layout.TilesX - 1);
        int pageY = Math.Min(sampleZ / pageSpan, mip0Layout.TilesY - 1);
        int localX = sampleX - pageX * pageSpan;
        int localY = sampleZ - pageY * pageSpan;
        var key = new TerrainPageKey(0, pageX, pageY);

        CachedPage page = GetOrLoadPage(key);
        int paddedX = localX + fileReader.HeightmapHeader.Padding;
        int paddedY = localY + fileReader.HeightmapHeader.Padding;
        ushort encodedHeight = page.Data[paddedY * paddedTileSize + paddedX];
        return encodedHeight * TerrainComponent.HeightSampleNormalization * heightScale;
    }

    private CachedPage GetOrLoadPage(TerrainPageKey key)
    {
        lock (gate)
        {
            if (pages.TryGetValue(key, out CachedPage? page))
            {
                Touch(page);
                return page;
            }

            var bytes = new byte[pageByteSize];
            fileReader.ReadHeightPage(key, bytes);
            var data = new ushort[paddedTileSize * paddedTileSize];
            MemoryMarshal.Cast<byte, ushort>(bytes).CopyTo(data);
            page = new CachedPage(key, data);
            pages.Add(key, page);
            page.Node = lru.AddLast(key);

            EvictIfNeeded();
            return page;
        }
    }

    private void Touch(CachedPage page)
    {
        if (page.Node == null || ReferenceEquals(page.Node, lru.Last))
            return;

        lru.Remove(page.Node);
        lru.AddLast(page.Node);
    }

    private void EvictIfNeeded()
    {
        while (pages.Count > MaxCachedPages && lru.First is { } oldest)
        {
            lru.RemoveFirst();
            pages.Remove(oldest.Value);
        }
    }

    private sealed class CachedPage
    {
        public CachedPage(TerrainPageKey key, ushort[] data)
        {
            Key = key;
            Data = data;
        }

        public TerrainPageKey Key { get; }
        public ushort[] Data { get; }
        public LinkedListNode<TerrainPageKey>? Node { get; set; }
    }
}
