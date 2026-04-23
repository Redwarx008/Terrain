using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Terrain.Editor.Models;
using Terrain.Editor.Services;
using Terrain.Shared;

namespace Terrain.Editor.Services.Export.Exporters;

/// <summary>
/// Exports the current editor terrain state to a .terrain runtime file.
/// </summary>
public class TerrainExporter : IExporter
{
    private const int HeightMapPadding = 2;
    // Runtime must honor the splat VT header instead of assuming the heightmap padding.
    private const int SplatMapPadding = 1;
    private const int DefaultTileSize = 129;

    /// <summary>
    /// The TerrainManager to export data from. Must be set before calling ExportAsync.
    /// </summary>
    public TerrainManager? TerrainManager { get; set; }

    public string Name => "Terrain";
    public string FileFilter => "Terrain Files (*.terrain)|*.terrain";
    public string DefaultExtension => "terrain";

    public async Task ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct)
    {
        var tm = TerrainManager ?? throw new InvalidOperationException("TerrainManager not set");
        ushort[] heightData = tm.HeightDataCache
            ?? throw new InvalidOperationException("No height data loaded");
        int width = tm.HeightCacheWidth;
        int height = tm.HeightCacheHeight;
        var detailControlMaps = tm.MaterialIndices
            ?? throw new InvalidOperationException("No material index map loaded");
        int leafNodeSize = tm.SplitConfig?.BaseChunkSize ?? SplitTerrainConfig.DefaultBaseChunkSize;

        byte[] detailIndexData = detailControlMaps.GetIndexRawData();
        byte[] detailWeightData = detailControlMaps.GetWeightRawData();
        int splatW = detailControlMaps.Width;
        int splatH = detailControlMaps.Height;

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // 1. Generate MinMaxErrorMaps (reuse existing Editor logic)
            progress.Report(ExportProgress.Running(0, 6, "Generating MinMaxErrorMap..."));
            var minMaxErrorMaps = HeightmapLoader.GenerateMinMaxErrorMaps(
                heightData, width, height, leafNodeSize);
            ct.ThrowIfCancellationRequested();

            // 2. Calculate mip levels
            int heightMapMipLevels = VirtualTextureLayout.GetMipCount(width, height, DefaultTileSize);
            int splatMapMipLevels = VirtualTextureLayout.GetMipCount(splatW, splatH, DefaultTileSize);

            // 4. Write the .terrain file
            progress.Report(ExportProgress.Running(1, 6, "Writing .terrain file..."));

            WriteTerrainFile(
                outputPath, width, height,
                heightData, detailIndexData, detailWeightData, splatW, splatH,
                minMaxErrorMaps, leafNodeSize,
                heightMapMipLevels, splatMapMipLevels,
                progress, ct);

            progress.Report(ExportProgress.Completed());
        }, ct);
    }

    private static void WriteTerrainFile(
        string outputPath,
        int width, int height,
        ushort[] heightData,
        byte[] detailIndexData,
        byte[] detailWeightData,
        int splatW, int splatH,
        EditorMinMaxErrorMap[] minMaxErrorMaps,
        int leafNodeSize,
        int heightMapMipLevels,
        int splatMapMipLevels,
        IProgress<ExportProgress> progress,
        CancellationToken ct)
    {
        using var fs = new FileStream(outputPath, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        // Write file header
        var header = new TerrainFileHeader
        {
            Magic = TerrainFileHeader.MAGIC_VALUE,
            Version = TerrainFileHeader.CURRENT_VERSION,
            Width = width,
            Height = height,
            LeafNodeSize = leafNodeSize,
            TileSize = DefaultTileSize,
            Padding = HeightMapPadding,
            HeightMapMipLevels = heightMapMipLevels,
            SplatMapFormat = (int)VTFormat.Rgba32,
            SplatMapMipLevels = splatMapMipLevels,
            SplatMapResolutionRatio = 1,
            Reserved2 = (int)VTFormat.Rgba32,
            Reserved3 = splatMapMipLevels,
        };
        WriteStruct(writer, ref header);

        // Write MinMaxErrorMap data
        progress.Report(ExportProgress.Running(2, 6, "Writing MinMaxErrorMap data..."));
        writer.Write(minMaxErrorMaps.Length);
        foreach (var map in minMaxErrorMaps)
        {
            map.WriteTo(writer);
        }

        // Write HeightMap VT data
        progress.Report(ExportProgress.Running(3, 6, "Writing HeightMap VT data..."));
        var heightVTHeader = new VTHeader
        {
            Width = width,
            Height = height,
            TileSize = DefaultTileSize,
            Padding = HeightMapPadding,
            BytesPerPixel = 2, // L16
            Mipmaps = heightMapMipLevels,
        };
        WriteStruct(writer, ref heightVTHeader);
        StreamMipLevels<ushort>(writer, heightData, width, height,
            DefaultTileSize, HeightMapPadding, ct);

        // Write SplatMap VT data
        progress.Report(ExportProgress.Running(4, 6, "Writing DetailIndex VT data..."));
        var splatVTHeader = new VTHeader
        {
            Width = splatW,
            Height = splatH,
            TileSize = DefaultTileSize,
            Padding = SplatMapPadding,
            BytesPerPixel = 1, // R8
            Mipmaps = splatMapMipLevels,
        };
        WriteStruct(writer, ref splatVTHeader);
        StreamMipLevels<byte>(writer, detailIndexData, splatW, splatH,
            DefaultTileSize, SplatMapPadding, ct);

        progress.Report(ExportProgress.Running(5, 6, "Writing DetailWeight VT data..."));
        var detailWeightVTHeader = new VTHeader
        {
            Width = splatW,
            Height = splatH,
            TileSize = DefaultTileSize,
            Padding = SplatMapPadding,
            BytesPerPixel = 4,
            Mipmaps = splatMapMipLevels,
        };
        WriteStruct(writer, ref detailWeightVTHeader);
        StreamMipLevels<byte>(writer, detailWeightData, splatW, splatH,
            DefaultTileSize, SplatMapPadding, ct);
    }

    /// <summary>
    /// Stream mip levels: generate one level, compute tiles in parallel, write, then next level.
    /// </summary>
    private static void StreamMipLevels<T>(
        BinaryWriter writer, T[] source, int srcW, int srcH,
        int tileSize, int padding, CancellationToken ct)
        where T : unmanaged
    {
        T[] currentLevel = source;
        int curW = srcW, curH = srcH;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Current level: parallel compute tiles, sequential write
            WriteLevelTilesParallel(writer, currentLevel, curW, curH, tileSize, padding);

            if (curW <= tileSize && curH <= tileSize)
                break;

            // Generate next mip level
            int nextW = (curW + 1) / 2;
            int nextH = (curH + 1) / 2;
            T[] nextLevel = GenerateNextMip(currentLevel, curW, curH, nextW, nextH);

            // Release non-source level
            if (!ReferenceEquals(currentLevel, source))
                currentLevel = null!;

            currentLevel = nextLevel;
            curW = nextW;
            curH = nextH;
        }
    }

    /// <summary>
    /// Coordinate-consistent mipmap generation.
    /// parent[x,y] = child[x*2, y*2], edge clamped.
    /// </summary>
    private static T[] GenerateNextMip<T>(T[] src, int srcW, int srcH, int dstW, int dstH)
        where T : unmanaged
    {
        var dst = new T[dstW * dstH];

        Parallel.For(0, dstH, y =>
        {
            int srcY = Math.Min(y * 2, srcH - 1);
            for (int x = 0; x < dstW; x++)
            {
                int srcX = Math.Min(x * 2, srcW - 1);
                dst[x + y * dstW] = src[srcX + srcY * srcW];
            }
        });

        return dst;
    }

    /// <summary>
    /// Compute tiles for one mip level in parallel, write sequentially.
    /// </summary>
    private static void WriteLevelTilesParallel<T>(
        BinaryWriter writer, T[] data, int w, int h,
        int tileSize, int padding)
        where T : unmanaged
    {
        int nTilesX = VirtualTextureLayout.ComputeTileCount(w, tileSize);
        int nTilesY = VirtualTextureLayout.ComputeTileCount(h, tileSize);
        int totalTiles = nTilesX * nTilesY;
        int paddedSize = tileSize + padding * 2;
        int effectiveTileSpan = tileSize - 1;
        int bytesPerPixel = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

        // Pre-compute all tiles in parallel
        var tiles = new byte[totalTiles][];

        Parallel.For(0, totalTiles, i =>
        {
            int ty = i / nTilesX;
            int tx = i % nTilesX;
            int originX = tx * effectiveTileSpan - padding;
            int originY = ty * effectiveTileSpan - padding;
            tiles[i] = ReadTile(data, w, h, originX, originY, paddedSize, bytesPerPixel);
        });

        // Sequential write (I/O order requirement)
        for (int i = 0; i < totalTiles; i++)
        {
            writer.Write(tiles[i]);
        }
    }

    /// <summary>
    /// Read a padded tile from source data with boundary clamping.
    /// </summary>
    private static byte[] ReadTile<T>(T[] data, int srcW, int srcH,
        int originX, int originY, int paddedSize, int bytesPerPixel)
        where T : unmanaged
    {
        var tilePixels = new T[paddedSize * paddedSize];

        for (int y = 0; y < paddedSize; y++)
        {
            int srcY = Math.Clamp(originY + y, 0, srcH - 1);
            for (int x = 0; x < paddedSize; x++)
            {
                int srcX = Math.Clamp(originX + x, 0, srcW - 1);
                tilePixels[x + y * paddedSize] = data[srcX + srcY * srcW];
            }
        }

        ReadOnlySpan<byte> byteView = MemoryMarshal.AsBytes(tilePixels.AsSpan());
        return byteView.ToArray();
    }

    private static void WriteStruct<T>(BinaryWriter writer, ref T value)
        where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
        writer.Write(bytes);
    }
}
