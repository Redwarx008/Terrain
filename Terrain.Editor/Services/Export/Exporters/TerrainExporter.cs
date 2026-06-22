#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Terrain.Editor.Models;
using Terrain.Editor.Services;

namespace Terrain.Editor.Services.Export.Exporters;

/// <summary>
/// Exports the current editor terrain state to a .terrain runtime file.
/// </summary>
public class TerrainExporter : IExporter
{
    private const int DefaultLeafNodeSize = 32;
    private const int DefaultTileSize = 129;
    private const int HeightMapPadding = 2;
    private const int DetailMapPadding = 1;
    private const int DetailMapResolutionRatio = 2;

    /// <summary>
    /// The TerrainManager to export data from. Must be set before calling ExportAsync.
    /// </summary>
    public TerrainManager? TerrainManager { get; set; }

    public string Name => "Terrain";
    public string FileFilter => "Terrain Files (*.terrain)|*.terrain";
    public string DefaultExtension => "terrain";

    public Task ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        TerrainManager tm = TerrainManager
            ?? throw new InvalidOperationException("Terrain manager is not set.");
        if (tm.HeightDataCache == null || tm.HeightCacheWidth <= 1 || tm.HeightCacheHeight <= 1)
            throw new InvalidOperationException("Heightmap data is not loaded; load authoring heightmap data before exporting .terrain.");
        if (tm.BiomeMask == null)
            throw new InvalidOperationException("Biome mask data is not loaded; export requires authoring biome_mask.png or an in-memory BiomeMask.");

        IReadOnlyList<BiomeRuleLayer> layersSnapshot = CreateLayerSnapshot(BiomeRuleService.Instance.Layers);
        if (layersSnapshot.Count == 0)
            throw new InvalidOperationException("Biome settings are not loaded; export requires biome_settings.toml or BiomeRuleService rules to bake DetailIndex and DetailWeight.");

        ushort[] heightData = tm.HeightDataCache.ToArray();
        int width = tm.HeightCacheWidth;
        int height = tm.HeightCacheHeight;
        float heightScale = tm.HeightScale;
        byte[] biomeMaskData = tm.BiomeMask.GetRawData().ToArray();
        int biomeMaskWidth = tm.BiomeMask.Width;
        int biomeMaskHeight = tm.BiomeMask.Height;

        return Task.Run(
            () => ExportBakedTerrain(
                outputPath,
                heightData,
                width,
                height,
                heightScale,
                biomeMaskData,
                biomeMaskWidth,
                biomeMaskHeight,
                layersSnapshot,
                progress,
                ct),
            ct);
    }

    internal static void ExportBakedTerrain(
        string outputPath,
        ushort[] heightData,
        int width,
        int height,
        float heightScale,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        IReadOnlyList<BiomeRuleLayer> layers,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(heightData);
        ArgumentNullException.ThrowIfNull(biomeMaskData);
        ArgumentNullException.ThrowIfNull(layers);
        ValidateDimensions(width, height, nameof(width), nameof(height));
        ValidateBufferLength(heightData.Length, width, height, "Heightmap", nameof(heightData));
        ValidateDimensions(biomeMaskWidth, biomeMaskHeight, nameof(biomeMaskWidth), nameof(biomeMaskHeight));
        ValidateBufferLength(biomeMaskData.Length, biomeMaskWidth, biomeMaskHeight, "Biome mask", nameof(biomeMaskData));
        if (layers.Count == 0)
            throw new InvalidOperationException("Biome settings are not loaded; export requires biome_settings.toml or BiomeRuleService rules to bake DetailIndex and DetailWeight.");

        ct.ThrowIfCancellationRequested();
        progress?.Report(ExportProgress.Running(1, 6, "Baking DetailTexture..."));
        BakedDetailMapData bakedDetail = BakedDetailMapBuilder.Generate(
            heightData,
            width,
            height,
            heightScale,
            biomeMaskData,
            biomeMaskWidth,
            biomeMaskHeight,
            layers);

        ct.ThrowIfCancellationRequested();
        progress?.Report(ExportProgress.Running(2, 6, "Generating MinMaxErrorMaps..."));
        EditorMinMaxErrorMap[] minMaxErrorMaps = HeightmapLoader.GenerateMinMaxErrorMaps(
            heightData,
            width,
            height,
            DefaultLeafNodeSize);

        ct.ThrowIfCancellationRequested();
        progress?.Report(ExportProgress.Running(3, 6, "Writing terrain header..."));
        WriteTerrainFile(
            outputPath,
            heightData,
            width,
            height,
            minMaxErrorMaps,
            bakedDetail,
            progress,
            ct);
        progress?.Report(ExportProgress.Completed());
    }

    private static void WriteTerrainFile(
        string outputPath,
        ushort[] heightData,
        int width,
        int height,
        EditorMinMaxErrorMap[] minMaxErrorMaps,
        BakedDetailMapData bakedDetail,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        int heightMapMipLevels = global::Terrain.VirtualTextureLayout.GetMipCount(width, height, DefaultTileSize);
        int detailMapMipLevels = global::Terrain.VirtualTextureLayout.GetMipCount(bakedDetail.Width, bakedDetail.Height, DefaultTileSize);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        var header = new TerrainFileHeader
        {
            Magic = TerrainFileHeader.MAGIC_VALUE,
            Version = TerrainFileHeader.CURRENT_VERSION,
            Width = width,
            Height = height,
            LeafNodeSize = DefaultLeafNodeSize,
            TileSize = DefaultTileSize,
            Padding = HeightMapPadding,
            HeightMapMipLevels = heightMapMipLevels,
            DetailMapFormat = (int)VTFormat.Rgba32,
            DetailMapMipLevels = detailMapMipLevels,
            DetailMapResolutionRatio = DetailMapResolutionRatio,
        };
        WriteStruct(writer, ref header);

        writer.Write(minMaxErrorMaps.Length);
        foreach (EditorMinMaxErrorMap map in minMaxErrorMaps)
            map.WriteTo(writer);

        progress?.Report(ExportProgress.Running(4, 6, "Writing HeightMap VT data..."));
        var heightHeader = new VTHeader
        {
            Width = width,
            Height = height,
            TileSize = DefaultTileSize,
            Padding = HeightMapPadding,
            BytesPerPixel = sizeof(ushort),
            Mipmaps = heightMapMipLevels,
        };
        WriteStruct(writer, ref heightHeader);
        StreamMipLevels<ushort>(
            writer,
            heightData,
            width,
            height,
            DefaultTileSize,
            HeightMapPadding,
            DownsampleHeights,
            ct);

        progress?.Report(ExportProgress.Running(5, 6, "Writing DetailIndex VT data..."));
        var detailIndexHeader = new VTHeader
        {
            Width = bakedDetail.Width,
            Height = bakedDetail.Height,
            TileSize = DefaultTileSize,
            Padding = DetailMapPadding,
            BytesPerPixel = 4,
            Mipmaps = detailMapMipLevels,
        };
        WriteStruct(writer, ref detailIndexHeader);
        StreamMipLevels<DetailControlPixel>(
            writer,
            bakedDetail.DetailIndex,
            bakedDetail.Width,
            bakedDetail.Height,
            DefaultTileSize,
            DetailMapPadding,
            DownsampleDetailControl,
            ct);

        progress?.Report(ExportProgress.Running(6, 6, "Writing DetailWeight VT data..."));
        var detailWeightHeader = detailIndexHeader;
        WriteStruct(writer, ref detailWeightHeader);
        StreamMipLevels<DetailControlPixel>(
            writer,
            bakedDetail.DetailWeight,
            bakedDetail.Width,
            bakedDetail.Height,
            DefaultTileSize,
            DetailMapPadding,
            DownsampleDetailControl,
            ct);
    }

    private static IReadOnlyList<BiomeRuleLayer> CreateLayerSnapshot(IEnumerable<BiomeRuleLayer> layers)
        => layers.Select(CloneLayer).ToArray();

    private static BiomeRuleLayer CloneLayer(BiomeRuleLayer source)
    {
        var clone = new BiomeRuleLayer
        {
            Id = source.Id,
            Name = source.Name,
            Enabled = source.Enabled,
            Visible = source.Visible,
            BiomeId = source.BiomeId,
            MaterialSlotIndex = source.MaterialSlotIndex,
            PriorityOrder = source.PriorityOrder,
        };

        foreach (BiomeModifier modifier in source.Modifiers)
            clone.Modifiers.Add(modifier.Clone());

        return clone;
    }

    private static void StreamMipLevels<T>(
        BinaryWriter writer,
        T[] mip0Data,
        int width,
        int height,
        int tileSize,
        int padding,
        Func<T[], int, int, T[]> downsample,
        CancellationToken ct)
        where T : unmanaged
    {
        T[] mipData = mip0Data;
        int mipWidth = width;
        int mipHeight = height;
        int mipCount = global::Terrain.VirtualTextureLayout.GetMipCount(width, height, tileSize);

        for (int mip = 0; mip < mipCount; mip++)
        {
            ValidateBufferLength(mipData.Length, mipWidth, mipHeight, "VT mip", nameof(mip0Data));
            WriteMipPages(writer, mipData, mipWidth, mipHeight, tileSize, padding, ct);

            if (mip + 1 < mipCount)
            {
                mipData = downsample(mipData, mipWidth, mipHeight);
                mipWidth = Math.Max(1, (mipWidth + 1) / 2);
                mipHeight = Math.Max(1, (mipHeight + 1) / 2);
            }
        }
    }

    private static void WriteMipPages<T>(
        BinaryWriter writer,
        T[] mipData,
        int mipWidth,
        int mipHeight,
        int tileSize,
        int padding,
        CancellationToken ct)
        where T : unmanaged
    {
        global::Terrain.VirtualTextureMipLayoutInfo layout =
            global::Terrain.VirtualTextureLayout.GetMipLayout(mipWidth, mipHeight, tileSize, 0);
        int paddedTileSize = checked(tileSize + padding * 2);
        var page = new T[checked(paddedTileSize * paddedTileSize)];
        int pageSpan = tileSize - 1;

        for (int pageY = 0; pageY < layout.TilesY; pageY++)
        {
            for (int pageX = 0; pageX < layout.TilesX; pageX++)
            {
                ct.ThrowIfCancellationRequested();
                int sourceOriginX = pageX * pageSpan;
                int sourceOriginY = pageY * pageSpan;

                for (int y = 0; y < paddedTileSize; y++)
                {
                    int sourceY = Math.Clamp(sourceOriginY + y - padding, 0, mipHeight - 1);
                    int destinationRow = y * paddedTileSize;
                    int sourceRow = sourceY * mipWidth;

                    for (int x = 0; x < paddedTileSize; x++)
                    {
                        int sourceX = Math.Clamp(sourceOriginX + x - padding, 0, mipWidth - 1);
                        page[destinationRow + x] = mipData[sourceRow + sourceX];
                    }
                }

                writer.Write(MemoryMarshal.AsBytes(page.AsSpan()));
            }
        }
    }

    private static ushort[] DownsampleHeights(ushort[] source, int width, int height)
    {
        int nextWidth = Math.Max(1, (width + 1) / 2);
        int nextHeight = Math.Max(1, (height + 1) / 2);
        var destination = new ushort[checked(nextWidth * nextHeight)];

        for (int y = 0; y < nextHeight; y++)
        {
            for (int x = 0; x < nextWidth; x++)
            {
                int sourceX = x * 2;
                int sourceY = y * 2;
                int sum = 0;
                int count = 0;

                for (int dy = 0; dy < 2; dy++)
                {
                    int yy = sourceY + dy;
                    if (yy >= height)
                        continue;

                    int row = yy * width;
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int xx = sourceX + dx;
                        if (xx >= width)
                            continue;

                        sum += source[row + xx];
                        count++;
                    }
                }

                destination[y * nextWidth + x] = (ushort)Math.Clamp((sum + count / 2) / Math.Max(1, count), 0, ushort.MaxValue);
            }
        }

        return destination;
    }

    private static DetailControlPixel[] DownsampleDetailControl(DetailControlPixel[] source, int width, int height)
    {
        int nextWidth = Math.Max(1, (width + 1) / 2);
        int nextHeight = Math.Max(1, (height + 1) / 2);
        var destination = new DetailControlPixel[checked(nextWidth * nextHeight)];

        for (int y = 0; y < nextHeight; y++)
        {
            int sourceY = Math.Min(y * 2, height - 1);
            for (int x = 0; x < nextWidth; x++)
            {
                int sourceX = Math.Min(x * 2, width - 1);
                destination[y * nextWidth + x] = source[sourceY * width + sourceX];
            }
        }

        return destination;
    }

    private static void WriteStruct<T>(BinaryWriter writer, ref T value)
        where T : unmanaged
    {
        ReadOnlySpan<T> span = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
        writer.Write(MemoryMarshal.AsBytes(span));
    }

    private static void ValidateDimensions(int width, int height, string widthName, string heightName)
    {
        if (width <= 1)
            throw new ArgumentOutOfRangeException(widthName, "Terrain export dimensions must be greater than one.");
        if (height <= 1)
            throw new ArgumentOutOfRangeException(heightName, "Terrain export dimensions must be greater than one.");
    }

    private static void ValidateBufferLength(int actualLength, int width, int height, string label, string parameterName)
    {
        long expected = checked((long)width * height);
        if (expected > int.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName, $"{label} dimensions {width}x{height} are too large.");
        if (actualLength != expected)
            throw new ArgumentException($"{label} length mismatch. Expected {expected} elements for {width}x{height}, got {actualLength}.", parameterName);
    }
}
