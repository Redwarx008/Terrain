using System.Runtime.InteropServices;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class BakedDetailTerrainFormatTests
{
    public static void RunAll()
    {
        TestHarness.Run("terrain format version is v8 baked detail", TerrainFormatVersionIsV8BakedDetail);
        TestHarness.Run("runtime streaming rejects pre baked detail terrain versions", RuntimeStreamingRejectsPreBakedDetailVersions);
        TestHarness.Run("terrain file reader exposes baked detail page reads", TerrainFileReaderExposesBakedDetailPageReads);
        TestHarness.Run("editor baked detail builder emits typed RGBA index and weight maps", EditorBakedDetailBuilderEmitsTypedRgbaIndexAndWeightMaps);
        TestHarness.Run("terrain file reader rejects missing baked detail streams", TerrainFileReaderRejectsMissingBakedDetailStreams);
        TestHarness.Run("terrain file reader rejects non rgba detail header format", TerrainFileReaderRejectsNonRgbaDetailHeaderFormat);
        TestHarness.Run("terrain file reader releases file handle after constructor failure", TerrainFileReaderReleasesFileHandleAfterConstructorFailure);
        TestHarness.Run("terrain exporter fails until baked detail payloads exist", TerrainExporterFailsUntilBakedDetailPayloadsExist);
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

    private static void EditorBakedDetailBuilderEmitsTypedRgbaIndexAndWeightMaps()
    {
        global::Terrain.Editor.Services.BiomeRuleService service = global::Terrain.Editor.Services.BiomeRuleService.Instance;
        service.ClearAll();
        service.AddBiomeFromConfig(1, "Default", new System.Numerics.Vector4(0, 1, 0, 1));
        service.AddLayerFromConfig(1, "Base", true, 0.0f, 1000.0f, 0.0f, 90.0f, 1.0f, 7);

        ushort[] heightData = new ushort[16];
        byte[] biomeMask = [1, 1, 1, 1];

        global::Terrain.Editor.Services.Export.BakedDetailMapData data =
            global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                heightData,
                4,
                4,
                100.0f,
                biomeMask,
                2,
                2,
                service.Layers);

        TestHarness.AssertEqual(2, data.Width, "detail width");
        TestHarness.AssertEqual(2, data.Height, "detail height");
        TestHarness.AssertEqual(4, data.DetailIndex.Length, "detail index pixel count");
        TestHarness.AssertEqual(4, data.DetailWeight.Length, "detail weight pixel count");

        global::Terrain.Editor.Services.Export.DetailControlPixel firstIndex = data.DetailIndex[0];
        global::Terrain.Editor.Services.Export.DetailControlPixel firstWeight = data.DetailWeight[0];
        TestHarness.AssertEqual(7, firstIndex.R, "first material index");
        TestHarness.AssertEqual(byte.MaxValue, firstIndex.G, "unused material index channel");
        TestHarness.AssertEqual(byte.MaxValue, firstWeight.R, "first material weight");
        TestHarness.AssertEqual(0, firstWeight.G, "unused material weight channel");

        int weightSum = firstWeight.R + firstWeight.G + firstWeight.B + firstWeight.A;
        TestHarness.Assert(weightSum is >= 254 and <= 255, $"encoded weights should be normalized, actual sum {weightSum}");
    }

    private static void TerrainFileReaderRejectsMissingBakedDetailStreams()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string missingWeightPath = Path.Combine(directory, "missing-detail-weight.terrain");
        WriteMinimalTerrainFile(missingWeightPath, writeDetailWeightHeader: false, writeDetailWeightPayload: false);
        InvalidDataException missingEx = TestHarness.AssertThrows<InvalidDataException>(
            () => new global::Terrain.TerrainFileReader(missingWeightPath).Dispose(),
            "reader should reject missing DetailWeight VT header");
        TestHarness.Assert(missingEx.Message.Contains("baked detail", StringComparison.OrdinalIgnoreCase), "missing detail payload error should mention baked detail data");

        string truncatedWeightPath = Path.Combine(directory, "truncated-detail-weight.terrain");
        WriteMinimalTerrainFile(truncatedWeightPath, writeDetailWeightHeader: true, writeDetailWeightPayload: false);
        InvalidDataException ex = TestHarness.AssertThrows<InvalidDataException>(
            () => new global::Terrain.TerrainFileReader(truncatedWeightPath).Dispose(),
            "reader should reject truncated DetailWeight VT payload");
        TestHarness.Assert(ex.Message.Contains("truncated", StringComparison.OrdinalIgnoreCase), "truncated detail payload error should mention truncation");
    }

    private static void TerrainFileReaderRejectsNonRgbaDetailHeaderFormat()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "bad-detail-format.terrain");
        WriteMinimalTerrainFile(path, writeDetailWeightHeader: true, writeDetailWeightPayload: true, detailMapFormat: 3);

        InvalidDataException ex = TestHarness.AssertThrows<InvalidDataException>(
            () => new global::Terrain.TerrainFileReader(path).Dispose(),
            "reader should reject non-RGBA detail map header format");
        TestHarness.Assert(ex.Message.Contains("detail map format", StringComparison.OrdinalIgnoreCase), "format error should name the detail map format");
    }

    private static void TerrainFileReaderReleasesFileHandleAfterConstructorFailure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string missingWeightPath = Path.Combine(directory, "missing-detail-weight.terrain");
        WriteMinimalTerrainFile(missingWeightPath, writeDetailWeightHeader: false, writeDetailWeightPayload: false);
        TestHarness.AssertThrows<InvalidDataException>(
            () => new global::Terrain.TerrainFileReader(missingWeightPath).Dispose(),
            "missing DetailWeight should fail construction");
        AssertCanReopenForExclusiveWrite(missingWeightPath);

        string badFormatPath = Path.Combine(directory, "bad-detail-format.terrain");
        WriteMinimalTerrainFile(badFormatPath, writeDetailWeightHeader: true, writeDetailWeightPayload: true, detailMapFormat: 3);
        TestHarness.AssertThrows<InvalidDataException>(
            () => new global::Terrain.TerrainFileReader(badFormatPath).Dispose(),
            "bad DetailMapFormat should fail construction");
        AssertCanReopenForExclusiveWrite(badFormatPath);
    }

    private static void TerrainExporterFailsUntilBakedDetailPayloadsExist()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-exporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "terrain.terrain");

        var exporter = new global::Terrain.Editor.Services.Export.Exporters.TerrainExporter();
        InvalidOperationException ex = TestHarness.AssertThrows<InvalidOperationException>(
            () => exporter.ExportAsync(outputPath, new Progress<global::Terrain.Editor.Services.Export.ExportProgress>(), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "terrain exporter should fail until baked DetailIndex and DetailWeight payloads are implemented");
        TestHarness.Assert(ex.Message.Contains("DetailIndex", StringComparison.Ordinal), "exporter failure should mention DetailIndex");
        TestHarness.Assert(ex.Message.Contains("DetailWeight", StringComparison.Ordinal), "exporter failure should mention DetailWeight");
        TestHarness.Assert(!File.Exists(outputPath), "exporter should not create a fake v8 terrain file");

        string exporterSource = ReadRepoText("Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs");
        TestHarness.Assert(!exporterSource.Contains("Writing BiomeMask VT data", StringComparison.Ordinal), "exporter should not keep the old BiomeMask VT write path");
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

    private static void AssertCanReopenForExclusiveWrite(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.WriteByte(0x54);
    }

    private static void WriteMinimalTerrainFile(
        string path,
        bool writeDetailWeightHeader,
        bool writeDetailWeightPayload,
        int detailMapFormat = 0)
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
            DetailMapFormat = detailMapFormat,
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
