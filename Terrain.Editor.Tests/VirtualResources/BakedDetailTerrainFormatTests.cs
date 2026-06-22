using System.Runtime.InteropServices;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class BakedDetailTerrainFormatTests
{
    public static void RunAll()
    {
        TestHarness.Run("terrain format version is v8 baked detail", TerrainFormatVersionIsV8BakedDetail);
        TestHarness.Run("runtime streaming rejects pre baked detail terrain versions", RuntimeStreamingRejectsPreBakedDetailVersions);
        TestHarness.Run("terrain file reader exposes baked detail page reads", TerrainFileReaderExposesBakedDetailPageReads);
        TestHarness.Run("terrain file reader rejects missing baked detail streams", TerrainFileReaderRejectsMissingBakedDetailStreams);
        TestHarness.Run("runtime source no longer contains generated detail map state", RuntimeSourceNoLongerContainsGeneratedDetailMapState);
    }

    private static void TerrainFormatVersionIsV8BakedDetail()
    {
        TestHarness.AssertEqual(8, global::Terrain.Editor.Models.TerrainFileHeader.CURRENT_VERSION, "editor terrain format version");
        string editorFormat = ReadRepoText("Terrain.Editor/Models/TerrainFileFormat.cs");
        TestHarness.Assert(editorFormat.Contains("DetailMapFormat", StringComparison.Ordinal), "editor header should use detail map naming");
        TestHarness.Assert(editorFormat.Contains("DetailMapMipLevels", StringComparison.Ordinal), "editor header should expose detail mip count");
        TestHarness.Assert(editorFormat.Contains("DetailMapResolutionRatio", StringComparison.Ordinal), "editor header should expose detail resolution ratio");
        TestHarness.Assert(!editorFormat.Contains("SplatMapFormat", StringComparison.Ordinal), "editor header should not expose splat/biome mask format");
        TestHarness.Assert(!editorFormat.Contains("RiverMapFormat", StringComparison.Ordinal), "terrain header should not keep unused river map payload fields");
    }

    private static void RuntimeStreamingRejectsPreBakedDetailVersions()
    {
        string streaming = ReadRepoText("Terrain/Streaming/TerrainStreaming.cs");
        TestHarness.Assert(streaming.Contains("MinSupportedVersion = 8", StringComparison.Ordinal), "runtime reader should reject old terrain files");
        TestHarness.Assert(streaming.Contains("MaxSupportedVersion = 8", StringComparison.Ordinal), "runtime reader should only accept current baked detail terrain version");
        TestHarness.Assert(streaming.Contains("re-export", StringComparison.OrdinalIgnoreCase), "old terrain version error should ask for re-export");
    }

    private static void TerrainFileReaderExposesBakedDetailPageReads()
    {
        string streaming = ReadRepoText("Terrain/Streaming/TerrainStreaming.cs");
        TestHarness.Assert(streaming.Contains("DetailIndexMapHeader", StringComparison.Ordinal), "reader should expose detail index header");
        TestHarness.Assert(streaming.Contains("DetailWeightMapHeader", StringComparison.Ordinal), "reader should expose detail weight header");
        TestHarness.Assert(streaming.Contains("ReadDetailIndexPage", StringComparison.Ordinal), "reader should read detail index pages");
        TestHarness.Assert(streaming.Contains("ReadDetailWeightPage", StringComparison.Ordinal), "reader should read detail weight pages");
        TestHarness.Assert(!streaming.Contains("ReadSplatMapPage", StringComparison.Ordinal), "reader should not expose old splat/biome mask pages");
    }

    private static void TerrainFileReaderRejectsMissingBakedDetailStreams()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string missingWeightPath = Path.Combine(directory, "missing-detail-weight.terrain");
        WriteMinimalTerrainFile(missingWeightPath, writeDetailWeightHeader: false, writeDetailWeightPayload: false);
        TestHarness.AssertThrows<EndOfStreamException>(
            () => new global::Terrain.TerrainFileReader(missingWeightPath).Dispose(),
            "reader should reject missing DetailWeight VT header");

        string truncatedWeightPath = Path.Combine(directory, "truncated-detail-weight.terrain");
        WriteMinimalTerrainFile(truncatedWeightPath, writeDetailWeightHeader: true, writeDetailWeightPayload: false);
        InvalidDataException ex = TestHarness.AssertThrows<InvalidDataException>(
            () => new global::Terrain.TerrainFileReader(truncatedWeightPath).Dispose(),
            "reader should reject truncated DetailWeight VT payload");
        TestHarness.Assert(ex.Message.Contains("truncated", StringComparison.OrdinalIgnoreCase), "truncated detail payload error should mention truncation");
    }

    private static void RuntimeSourceNoLongerContainsGeneratedDetailMapState()
    {
        string streaming = ReadRepoText("Terrain/Streaming/TerrainStreaming.cs");
        string processor = ReadRepoText("Terrain/Core/TerrainProcessor.cs");
        TestHarness.Assert(!streaming.Contains("generatedDetailMaps", StringComparison.Ordinal), "streaming should not store generated detail maps");
        TestHarness.Assert(!streaming.Contains("FillGeneratedDetailPage", StringComparison.Ordinal), "streaming should not synthesize detail pages");
        TestHarness.Assert(!streaming.Contains("SetGeneratedDetailMaps", StringComparison.Ordinal), "streaming should not accept generated detail maps");
        TestHarness.Assert(!processor.Contains("RuntimeDetailMapBuilder.Generate", StringComparison.Ordinal), "runtime processor should not build detail maps");
        TestHarness.Assert(!processor.Contains("DetailMapBuilder", StringComparison.Ordinal), "runtime processor should not carry a detail builder delegate");
    }

    private static void WriteMinimalTerrainFile(string path, bool writeDetailWeightHeader, bool writeDetailWeightPayload)
    {
        const int heightWidth = 33;
        const int heightHeight = 33;
        const int tileSize = 129;
        const int heightPadding = 1;
        const int detailPadding = 1;
        const int detailWidth = 17;
        const int detailHeight = 17;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs);

        var header = new global::Terrain.TerrainFileHeader
        {
            Magic = global::Terrain.TerrainFileHeader.MagicValue,
            Version = 8,
            Width = heightWidth,
            Height = heightHeight,
            LeafNodeSize = 32,
            TileSize = tileSize,
            Padding = heightPadding,
            HeightMapMipLevels = 1,
            DetailMapFormat = 0,
            DetailMapMipLevels = 1,
            DetailMapResolutionRatio = 2,
        };
        WriteStruct(writer, header);

        writer.Write(1);
        writer.Write(1);
        writer.Write(1);
        var minMax = new global::Terrain.TerrainMinMaxErrorMap(1, 1);
        minMax.Set(0, 0, 0.0f, 1.0f, 0.0f);
        writer.Write(minMax.GetByteView());

        var heightHeader = new global::Terrain.TerrainVirtualTextureHeader
        {
            Width = heightWidth,
            Height = heightHeight,
            TileSize = tileSize,
            Padding = heightPadding,
            BytesPerPixel = sizeof(ushort),
            Mipmaps = 1,
        };
        WriteStruct(writer, heightHeader);
        writer.Write(new byte[ComputeTileByteSize(heightHeader)]);

        var detailHeader = new global::Terrain.TerrainVirtualTextureHeader
        {
            Width = detailWidth,
            Height = detailHeight,
            TileSize = tileSize,
            Padding = detailPadding,
            BytesPerPixel = 4,
            Mipmaps = 1,
        };
        WriteStruct(writer, detailHeader);
        writer.Write(new byte[ComputeTileByteSize(detailHeader)]);

        if (!writeDetailWeightHeader)
            return;

        WriteStruct(writer, detailHeader);
        if (writeDetailWeightPayload)
            writer.Write(new byte[ComputeTileByteSize(detailHeader)]);
    }

    private static void WriteStruct<T>(BinaryWriter writer, T value)
        where T : unmanaged
    {
        ReadOnlySpan<T> span = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
        writer.Write(MemoryMarshal.AsBytes(span));
    }

    private static int ComputeTileByteSize(global::Terrain.TerrainVirtualTextureHeader header)
    {
        int paddedTileSize = checked(header.TileSize + header.Padding * 2);
        return checked(paddedTileSize * paddedTileSize * header.BytesPerPixel);
    }

    internal static string ReadRepoText(string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), normalized));
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Terrain.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
