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
        TestHarness.Run("editor baked detail builder packs ordered layers deterministically", EditorBakedDetailBuilderPacksOrderedLayersDeterministically);
        TestHarness.Run("editor baked detail builder keeps only top four aggregated materials", EditorBakedDetailBuilderKeepsOnlyTopFourAggregatedMaterials);
        TestHarness.Run("editor baked detail builder aggregates duplicate material before top four selection", EditorBakedDetailBuilderAggregatesDuplicateMaterialBeforeTopFourSelection);
        TestHarness.Run("editor baked detail builder preserves stable ordering for equal priorities", EditorBakedDetailBuilderPreservesStableOrderingForEqualPriorities);
        TestHarness.Run("editor baked detail builder function overload preserves validation contract", EditorBakedDetailBuilderFunctionOverloadPreservesValidationContract);
        TestHarness.Run("editor baked detail builder fast height path matches functional height path", EditorBakedDetailBuilderFastHeightPathMatchesFunctionalHeightPath);
        TestHarness.Run("editor baked detail builder keeps parallel row evaluation", EditorBakedDetailBuilderKeepsParallelRowEvaluation);
        TestHarness.Run("editor baked detail builder large parallel path preserves direct validation exceptions", EditorBakedDetailBuilderLargeParallelPathPreservesDirectValidationExceptions);
        TestHarness.Run("editor baked detail builder function overload keeps large maps serial", EditorBakedDetailBuilderFunctionOverloadKeepsLargeMapsSerial);
        TestHarness.Run("editor baked detail builder avoids per pixel collection sorting", EditorBakedDetailBuilderAvoidsPerPixelCollectionSorting);
        TestHarness.Run("editor baked detail builder rejects invalid material slots", EditorBakedDetailBuilderRejectsInvalidMaterialSlots);
        TestHarness.Run("editor baked detail builder rejects unsupported texture masks", EditorBakedDetailBuilderRejectsUnsupportedTextureMasks);
        TestHarness.Run("editor baked detail builder detail pixel is four bytes", EditorBakedDetailBuilderDetailPixelIsFourBytes);
        TestHarness.Run("editor baked detail builder requires biome mask detail dimensions", EditorBakedDetailBuilderRequiresBiomeMaskDetailDimensions);
        TestHarness.Run("terrain file reader rejects missing baked detail streams", TerrainFileReaderRejectsMissingBakedDetailStreams);
        TestHarness.Run("terrain file reader rejects non rgba detail header format", TerrainFileReaderRejectsNonRgbaDetailHeaderFormat);
        TestHarness.Run("terrain file reader rejects trailing bytes after baked detail payloads", TerrainFileReaderRejectsTrailingBytesAfterBakedDetailPayloads);
        TestHarness.Run("terrain file reader releases file handle after constructor failure", TerrainFileReaderReleasesFileHandleAfterConstructorFailure);
        TestHarness.Run("terrain exporter writes baked detail VT payloads readable by runtime", TerrainExporterWritesBakedDetailVtPayloadsReadableByRuntime);
        TestHarness.Run("terrain exporter height mips preserve aligned source samples", TerrainExporterHeightMipsPreserveAlignedSourceSamples);
        TestHarness.Run("terrain exporter writes aligned height mips into terrain payload", TerrainExporterWritesAlignedHeightMipsIntoTerrainPayload);
        TestHarness.Run("terrain exporter supports even heightmap dimensions with aligned mips", TerrainExporterSupportsEvenHeightmapDimensionsWithAlignedMips);
        TestHarness.Run("terrain exporter aggregates detail mip contributions instead of copying top left texel", TerrainExporterAggregatesDetailMipContributionsInsteadOfCopyingTopLeftTexel);
        TestHarness.Run("terrain exporter aggregates multi channel detail mip top four", TerrainExporterAggregatesMultiChannelDetailMipTopFour);
        TestHarness.Run("terrain exporter sorts equal weight detail mip contributions by material index", TerrainExporterSortsEqualWeightDetailMipContributionsByMaterialIndex);
        TestHarness.Run("terrain exporter cancellation preserves existing target and deletes temp file", TerrainExporterCancellationPreservesExistingTargetAndDeletesTempFile);
        TestHarness.Run("terrain exporter source writes detail index and weight VT payloads", TerrainExporterSourceWritesDetailIndexAndWeightVtPayloads);
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

    private static void EditorBakedDetailBuilderPacksOrderedLayersDeterministically()
    {
        global::Terrain.Editor.Services.BiomeRuleLayer lowPriorityDuplicate = CreateLayer(0, materialSlotIndex: 5, priority: 0, CreateWeightModifier(0.5f));
        global::Terrain.Editor.Services.BiomeRuleLayer middlePriority = CreateLayer(0, materialSlotIndex: 6, priority: 1, CreateWeightModifier(0.5f));
        global::Terrain.Editor.Services.BiomeRuleLayer highPriorityDuplicate = CreateLayer(0, materialSlotIndex: 5, priority: 2, CreateWeightModifier(0.5f));

        global::Terrain.Editor.Services.Export.BakedDetailMapData data =
            global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                new ushort[16],
                4,
                4,
                100.0f,
                [0, 0, 0, 0],
                2,
                2,
                [lowPriorityDuplicate, middlePriority, highPriorityDuplicate]);

        global::Terrain.Editor.Services.Export.DetailControlPixel index = data.DetailIndex[0];
        global::Terrain.Editor.Services.Export.DetailControlPixel weight = data.DetailWeight[0];

        TestHarness.AssertEqual(5, index.R, "duplicate material should stay in the first slot");
        TestHarness.AssertEqual(6, index.G, "middle layer material should stay in the second slot");
        TestHarness.AssertEqual(byte.MaxValue, index.B, "unused third material slot");
        TestHarness.AssertEqual(byte.MaxValue, index.A, "unused fourth material slot");
        TestHarness.AssertEqual(191, weight.R, "duplicate material contributions should merge before normalization");
        TestHarness.AssertEqual(64, weight.G, "remaining layer weight should normalize to the second channel");
        TestHarness.AssertEqual(0, weight.B, "unused third weight slot");
        TestHarness.AssertEqual(0, weight.A, "unused fourth weight slot");
    }

    private static void EditorBakedDetailBuilderKeepsOnlyTopFourAggregatedMaterials()
    {
        global::Terrain.Editor.Services.Export.BakedDetailMapData data = GeneratePackingCase(
        [
            CreateLayer(0, materialSlotIndex: 6, priority: 0),
            CreateLayer(0, materialSlotIndex: 5, priority: 1, CreateWeightModifier(0.5f)),
            CreateLayer(0, materialSlotIndex: 4, priority: 2, CreateWeightModifier(0.5f)),
            CreateLayer(0, materialSlotIndex: 3, priority: 3, CreateWeightModifier(0.5f)),
            CreateLayer(0, materialSlotIndex: 2, priority: 4, CreateWeightModifier(0.5f)),
            CreateLayer(0, materialSlotIndex: 1, priority: 5, CreateWeightModifier(0.5f)),
        ]);

        global::Terrain.Editor.Services.Export.DetailControlPixel index = data.DetailIndex[0];
        TestHarness.AssertEqual(1, index.R, "highest aggregate material");
        TestHarness.AssertEqual(2, index.G, "second aggregate material");
        TestHarness.AssertEqual(3, index.B, "third aggregate material");
        TestHarness.AssertEqual(4, index.A, "fourth aggregate material");
    }

    private static void EditorBakedDetailBuilderAggregatesDuplicateMaterialBeforeTopFourSelection()
    {
        global::Terrain.Editor.Services.Export.BakedDetailMapData data = GeneratePackingCase(
        [
            CreateLayer(0, materialSlotIndex: 8, priority: 0),
            CreateLayer(0, materialSlotIndex: 9, priority: 1, CreateWeightModifier(0.2f)),
            CreateLayer(0, materialSlotIndex: 9, priority: 2, CreateWeightModifier(0.2f)),
            CreateLayer(0, materialSlotIndex: 4, priority: 3, CreateWeightModifier(0.2f)),
            CreateLayer(0, materialSlotIndex: 3, priority: 4, CreateWeightModifier(0.2f)),
            CreateLayer(0, materialSlotIndex: 2, priority: 5, CreateWeightModifier(0.2f)),
            CreateLayer(0, materialSlotIndex: 1, priority: 6, CreateWeightModifier(0.2f)),
        ]);

        global::Terrain.Editor.Services.Export.DetailControlPixel index = data.DetailIndex[0];
        global::Terrain.Editor.Services.Export.DetailControlPixel weight = data.DetailWeight[0];

        TestHarness.AssertEqual(8, index.R, "remaining coverage material should be top aggregate");
        TestHarness.AssertEqual(1, index.G, "first processed material should stay second after aggregation");
        TestHarness.AssertEqual(2, index.B, "second processed material should stay third after aggregation");
        TestHarness.AssertEqual(9, index.A, "duplicate material should enter top four after aggregate weight is considered");
        TestHarness.AssertEqual(87, weight.R, "remaining coverage normalized weight");
        TestHarness.AssertEqual(66, weight.G, "first layer normalized weight");
        TestHarness.AssertEqual(53, weight.B, "second layer normalized weight");
        TestHarness.AssertEqual(49, weight.A, "duplicate aggregate normalized weight");
    }

    private static void EditorBakedDetailBuilderPreservesStableOrderingForEqualPriorities()
    {
        global::Terrain.Editor.Services.Export.BakedDetailMapData data =
            global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                new ushort[16],
                4,
                4,
                100.0f,
                [0, 0, 0, 0],
                2,
                2,
                [
                    CreateLayer(0, materialSlotIndex: 1, priority: 10),
                    CreateLayer(0, materialSlotIndex: 2, priority: 10),
                    CreateLayer(0, materialSlotIndex: 3, priority: 10),
                ]);

        global::Terrain.Editor.Services.Export.DetailControlPixel index = data.DetailIndex[0];
        TestHarness.AssertEqual(3, index.R, "stable priority ordering should let the last same-priority layer retain top coverage");
        TestHarness.AssertEqual(byte.MaxValue, index.G, "full top coverage should stop before earlier equal-priority layers");
    }

    private static void EditorBakedDetailBuilderFunctionOverloadPreservesValidationContract()
    {
        ArgumentNullException nullHeight = TestHarness.AssertThrows<ArgumentNullException>(
            () => global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                (Func<int, int, float>)null!,
                4,
                4,
                [0, 0, 0, 0],
                2,
                2,
                [CreateLayer(0, materialSlotIndex: 1, priority: 0)]),
            "function overload should reject null getHeight before computing detail dimensions");
        TestHarness.AssertEqual("getHeight", nullHeight.ParamName, "null getHeight parameter name");

        ArgumentOutOfRangeException badWidth = TestHarness.AssertThrows<ArgumentOutOfRangeException>(
            () => global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                static (_, _) => 0.0f,
                0,
                4,
                [0, 0],
                1,
                2,
                [CreateLayer(0, materialSlotIndex: 1, priority: 0)]),
            "function overload should reject invalid height width before validating detail dimensions");
        TestHarness.AssertEqual("heightWidth", badWidth.ParamName, "invalid width parameter name");

        ArgumentOutOfRangeException badHeight = TestHarness.AssertThrows<ArgumentOutOfRangeException>(
            () => global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                static (_, _) => 0.0f,
                4,
                0,
                [0, 0],
                2,
                1,
                [CreateLayer(0, materialSlotIndex: 1, priority: 0)]),
            "function overload should reject invalid height height before validating detail dimensions");
        TestHarness.AssertEqual("heightHeight", badHeight.ParamName, "invalid height parameter name");
    }

    private static void EditorBakedDetailBuilderFastHeightPathMatchesFunctionalHeightPath()
    {
        const int heightSize = 9;
        const int detailSize = 5;
        ushort[] heightData = new ushort[heightSize * heightSize];
        for (int y = 0; y < heightSize; y++)
        {
            for (int x = 0; x < heightSize; x++)
                heightData[y * heightSize + x] = (ushort)(1000 + x * 251 + y * 617 + x * y * 43);
        }

        byte[] biomeMask = new byte[detailSize * detailSize];
        for (int i = 0; i < biomeMask.Length; i++)
            biomeMask[i] = (byte)(i % 2);

        const float heightScale = 200.0f;
        float heightScaleFactor = heightScale / ushort.MaxValue;
        global::Terrain.Editor.Services.BiomeRuleLayer[] layers =
        [
            CreateLayer(
                0,
                materialSlotIndex: 3,
                priority: 0,
                CreateModifier(global::Terrain.Editor.Services.BiomeModifierType.HeightRange, min: 5.0f, max: 55.0f, minFalloff: 8.0f, maxFalloff: 13.0f),
                CreateModifier(global::Terrain.Editor.Services.BiomeModifierType.SlopeRange, min: 2.0f, max: 70.0f, minFalloff: 3.0f, maxFalloff: 4.0f)),
            CreateLayer(
                0,
                materialSlotIndex: 4,
                priority: 1,
                CreateModifier(global::Terrain.Editor.Services.BiomeModifierType.CurvatureRange, min: 0.15f, max: 0.85f, minFalloff: 0.2f, maxFalloff: 0.1f, radius: 2.0f),
                CreateModifier(global::Terrain.Editor.Services.BiomeModifierType.DirectionRange, min: 0.0f, max: 1.0f, angleDegrees: 30.0f, angleRangeDegrees: 160.0f)),
            CreateLayer(
                1,
                materialSlotIndex: 7,
                priority: 2,
                CreateModifier(global::Terrain.Editor.Services.BiomeModifierType.Noise, min: 0.0f, max: 1.0f, scale: 0.17f, seed: 11.0f, octaves: 3.0f),
                CreateModifier(global::Terrain.Editor.Services.BiomeModifierType.HeightRange, min: 0.0f, max: 100.0f, minFalloff: 5.0f, maxFalloff: 5.0f, invert: 1.0f, opacity: 0.4f)),
            CreateLayer(
                1,
                materialSlotIndex: 8,
                priority: 3,
                CreateModifier(global::Terrain.Editor.Services.BiomeModifierType.SlopeRange, min: 0.0f, max: 90.0f, minFalloff: 1.0f, maxFalloff: 1.0f, blendMode: global::Terrain.Editor.Services.BiomeModifierBlendMode.Max, opacity: 0.7f)),
        ];

        global::Terrain.Editor.Services.Export.BakedDetailMapData fast =
            global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                heightData,
                heightSize,
                heightSize,
                heightScale,
                biomeMask,
                detailSize,
                detailSize,
                layers);

        global::Terrain.Editor.Services.Export.BakedDetailMapData functional =
            global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                (x, y) => heightData[y * heightSize + x] * heightScaleFactor,
                heightSize,
                heightSize,
                biomeMask,
                detailSize,
                detailSize,
                layers);

        TestHarness.AssertEqual(fast.Width, functional.Width, "detail width");
        TestHarness.AssertEqual(fast.Height, functional.Height, "detail height");
        for (int i = 0; i < fast.DetailIndex.Length; i++)
        {
            TestHarness.AssertEqual(fast.DetailIndex[i], functional.DetailIndex[i], $"detail index texel {i}");
            TestHarness.AssertEqual(fast.DetailWeight[i], functional.DetailWeight[i], $"detail weight texel {i}");
        }
    }

    private static void EditorBakedDetailBuilderAvoidsPerPixelCollectionSorting()
    {
        string builderSource = ReadRepoText("Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs");
        TestHarness.Assert(!builderSource.Contains("new List<MaterialContribution>", StringComparison.Ordinal), "builder should not allocate a contribution list per detail texel");
        TestHarness.Assert(!builderSource.Contains("OrderByDescending(static contribution => contribution.Weight)", StringComparison.Ordinal), "builder should not sort material contributions with LINQ per detail texel");
        TestHarness.Assert(!builderSource.Contains("Take(4)", StringComparison.Ordinal), "builder should use fixed top-four selection instead of LINQ Take per texel");

        string exporterSource = ReadRepoText("Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs");
        TestHarness.Assert(!exporterSource.Contains("new Dictionary<byte, int>", StringComparison.Ordinal), "detail mip aggregation should not allocate a dictionary per destination texel");
        TestHarness.Assert(!exporterSource.Contains("OrderByDescending(static contribution => contribution.Weight)", StringComparison.Ordinal), "detail mip aggregation should not sort contributions with LINQ per texel");
    }

    private static void EditorBakedDetailBuilderKeepsParallelRowEvaluation()
    {
        string builderSource = ReadRepoText("Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs");
        TestHarness.Assert(builderSource.Contains("Parallel.For", StringComparison.Ordinal), "builder should keep parallel row evaluation for large detail maps");
        TestHarness.Assert(builderSource.Contains("ParallelDetailTexelThreshold", StringComparison.Ordinal), "builder should gate parallel row evaluation behind a threshold");
        TestHarness.Assert(builderSource.Contains("CanEvaluateInParallel", StringComparison.Ordinal), "function-backed height evaluation should not be forced into parallel execution");

        string exporterSource = ReadRepoText("Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs");
        TestHarness.Assert(exporterSource.Contains("Parallel.For", StringComparison.Ordinal), "detail mip aggregation should keep parallel row evaluation for large mips");
        TestHarness.Assert(exporterSource.Contains("ParallelDetailMipTexelThreshold", StringComparison.Ordinal), "detail mip aggregation should gate parallel row evaluation behind a threshold");
    }

    private static void EditorBakedDetailBuilderLargeParallelPathPreservesDirectValidationExceptions()
    {
        const int heightSize = 513;
        const int detailSize = 257;
        ushort[] heightData = new ushort[heightSize * heightSize];
        byte[] biomeMask = Enumerable.Repeat((byte)0, detailSize * detailSize).ToArray();

        ArgumentOutOfRangeException invalidSlot = TestHarness.AssertThrows<ArgumentOutOfRangeException>(
            () => global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                heightData,
                heightSize,
                heightSize,
                100.0f,
                biomeMask,
                detailSize,
                detailSize,
                [CreateLayer(0, materialSlotIndex: 255, priority: 0)]),
            "large parallel builder should preserve direct invalid material slot exceptions");
        TestHarness.AssertEqual("materialSlotIndex", invalidSlot.ParamName, "parallel invalid slot exception parameter");

        var textureMaskLayer = CreateLayer(
            0,
            materialSlotIndex: 1,
            priority: 0,
            new global::Terrain.Editor.Services.BiomeModifier
            {
                Type = global::Terrain.Editor.Services.BiomeModifierType.TextureMask,
                Enabled = true,
                Visible = true,
            });

        TestHarness.AssertThrows<NotSupportedException>(
            () => global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                heightData,
                heightSize,
                heightSize,
                100.0f,
                biomeMask,
                detailSize,
                detailSize,
                [textureMaskLayer]),
            "large parallel builder should preserve direct TextureMask exceptions");

        global::Terrain.Editor.Services.Export.BakedDetailMapData shielded =
            global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                heightData,
                heightSize,
                heightSize,
                100.0f,
                biomeMask,
                detailSize,
                detailSize,
                [
                    CreateLayer(0, materialSlotIndex: 255, priority: 0),
                    CreateLayer(0, materialSlotIndex: 1, priority: 1),
                ]);
        TestHarness.AssertEqual(1, shielded.DetailIndex[0].R, "higher priority full-coverage layer should hide lower invalid layer");
    }

    private static void EditorBakedDetailBuilderFunctionOverloadKeepsLargeMapsSerial()
    {
        const int heightSize = 513;
        const int detailSize = 257;
        byte[] biomeMask = Enumerable.Repeat((byte)0, detailSize * detailSize).ToArray();
        int activeHeightCalls = 0;
        int overlappingHeightCalls = 0;

        float GetHeight(int x, int y)
        {
            if (Interlocked.Increment(ref activeHeightCalls) > 1)
                Interlocked.Exchange(ref overlappingHeightCalls, 1);

            Thread.SpinWait(32);
            Interlocked.Decrement(ref activeHeightCalls);
            return x + y;
        }

        _ = global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
            GetHeight,
            heightSize,
            heightSize,
            biomeMask,
            detailSize,
            detailSize,
            [CreateLayer(0, materialSlotIndex: 1, priority: 0)]);

        TestHarness.AssertEqual(0, overlappingHeightCalls, "function height overload should keep large maps serial");
    }

    private static void EditorBakedDetailBuilderRejectsInvalidMaterialSlots()
    {
        ArgumentOutOfRangeException negative = TestHarness.AssertThrows<ArgumentOutOfRangeException>(
            () => GenerateSingleLayer(CreateLayer(0, materialSlotIndex: -1, priority: 0)),
            "builder should reject negative material slots");
        TestHarness.Assert(negative.Message.Contains("material slot", StringComparison.OrdinalIgnoreCase), "negative slot error should mention material slot");

        ArgumentOutOfRangeException sentinel = TestHarness.AssertThrows<ArgumentOutOfRangeException>(
            () => GenerateSingleLayer(CreateLayer(0, materialSlotIndex: 255, priority: 0)),
            "builder should reject material slot 255 sentinel");
        TestHarness.Assert(sentinel.Message.Contains("0..254", StringComparison.Ordinal), "sentinel slot error should mention valid range");
    }

    private static void EditorBakedDetailBuilderRejectsUnsupportedTextureMasks()
    {
        var layer = CreateLayer(
            0,
            materialSlotIndex: 1,
            priority: 0,
            new global::Terrain.Editor.Services.BiomeModifier
            {
                Type = global::Terrain.Editor.Services.BiomeModifierType.TextureMask,
                Enabled = true,
                Visible = true,
            });

        NotSupportedException ex = TestHarness.AssertThrows<NotSupportedException>(
            () => GenerateSingleLayer(layer),
            "builder should reject active TextureMask modifiers until mask sampling is implemented");
        TestHarness.Assert(
            ex.Message.Contains("baked detail export does not support texture mask modifiers yet", StringComparison.OrdinalIgnoreCase),
            "TextureMask error should clearly describe unsupported baked detail export path");
    }

    private static void EditorBakedDetailBuilderDetailPixelIsFourBytes()
    {
        TestHarness.AssertEqual(4, Marshal.SizeOf<global::Terrain.Editor.Services.Export.DetailControlPixel>(), "DetailControlPixel size");
    }

    private static void EditorBakedDetailBuilderRequiresBiomeMaskDetailDimensions()
    {
        ArgumentException ex = TestHarness.AssertThrows<ArgumentException>(
            () => global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
                new ushort[16],
                4,
                4,
                100.0f,
                new byte[16],
                4,
                4,
                [CreateLayer(0, materialSlotIndex: 1, priority: 0)]),
            "builder should reject full-resolution biome masks");
        TestHarness.Assert(ex.Message.Contains("same dimensions as the baked detail map", StringComparison.OrdinalIgnoreCase), "dimension error should explain required mask/detail match");
    }

    private static void GenerateSingleLayer(global::Terrain.Editor.Services.BiomeRuleLayer layer)
    {
        _ = GeneratePackingCase([layer]);
    }

    private static global::Terrain.Editor.Services.Export.BakedDetailMapData GeneratePackingCase(global::Terrain.Editor.Services.BiomeRuleLayer[] layers)
    {
        return global::Terrain.Editor.Services.Export.BakedDetailMapBuilder.Generate(
            new ushort[16],
            4,
            4,
            100.0f,
            [0, 0, 0, 0],
            2,
            2,
            layers);
    }

    private static global::Terrain.Editor.Services.BiomeRuleLayer CreateLayer(
        int biomeId,
        int materialSlotIndex,
        int priority,
        params global::Terrain.Editor.Services.BiomeModifier[] modifiers)
    {
        var layer = new global::Terrain.Editor.Services.BiomeRuleLayer
        {
            BiomeId = biomeId,
            MaterialSlotIndex = materialSlotIndex,
            PriorityOrder = priority,
            Enabled = true,
            Visible = true,
        };

        layer.Modifiers.AddRange(modifiers);
        return layer;
    }

    private static global::Terrain.Editor.Services.BiomeModifier CreateWeightModifier(float weight)
    {
        return new global::Terrain.Editor.Services.BiomeModifier
        {
            Type = global::Terrain.Editor.Services.BiomeModifierType.HeightRange,
            BlendMode = global::Terrain.Editor.Services.BiomeModifierBlendMode.Multiply,
            Enabled = true,
            Visible = true,
            Min = 1000.0f,
            Max = 1000.0f,
            MinFalloff = 0.001f,
            MaxFalloff = 0.001f,
            Opacity = 1.0f - weight,
        };
    }

    private static global::Terrain.Editor.Services.BiomeModifier CreateModifier(
        global::Terrain.Editor.Services.BiomeModifierType type,
        float min,
        float max,
        float minFalloff = 0.001f,
        float maxFalloff = 0.001f,
        float radius = 1.0f,
        float angleDegrees = 0.0f,
        float angleRangeDegrees = 180.0f,
        float scale = 1.0f,
        float seed = 0.0f,
        float octaves = 1.0f,
        float invert = 0.0f,
        float opacity = 1.0f,
        global::Terrain.Editor.Services.BiomeModifierBlendMode blendMode = global::Terrain.Editor.Services.BiomeModifierBlendMode.Multiply)
    {
        return new global::Terrain.Editor.Services.BiomeModifier
        {
            Type = type,
            BlendMode = blendMode,
            Enabled = true,
            Visible = true,
            Min = min,
            Max = max,
            MinFalloff = minFalloff,
            MaxFalloff = maxFalloff,
            Radius = radius,
            AngleDegrees = angleDegrees,
            AngleRangeDegrees = angleRangeDegrees,
            Scale = scale,
            Seed = seed,
            Octaves = octaves,
            Invert = invert,
            Opacity = opacity,
        };
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

    private static void TerrainFileReaderRejectsTrailingBytesAfterBakedDetailPayloads()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "trailing-bytes.terrain");
        WriteMinimalTerrainFile(path, writeDetailWeightHeader: true, writeDetailWeightPayload: true);
        File.AppendAllBytes(path, [0xBA, 0xAD, 0xF0, 0x0D]);

        InvalidDataException ex = TestHarness.AssertThrows<InvalidDataException>(
            () => new global::Terrain.TerrainFileReader(path).Dispose(),
            "reader should reject bytes after the final DetailWeight payload");
        TestHarness.Assert(ex.Message.Contains("trailing", StringComparison.OrdinalIgnoreCase), "trailing data error should mention trailing bytes");
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

    private static void TerrainExporterWritesBakedDetailVtPayloadsReadableByRuntime()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-exporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "terrain.terrain");

        ushort[] heightData = new ushort[33 * 33];
        for (int i = 0; i < heightData.Length; i++)
            heightData[i] = (ushort)(i % ushort.MaxValue);

        byte[] biomeMask = Enumerable.Repeat((byte)0, 17 * 17).ToArray();
        var progress = new RecordingExportProgress();

        global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportBakedTerrain(
            outputPath,
            heightData,
            33,
            33,
            100.0f,
            biomeMask,
            17,
            17,
            [CreateLayer(0, materialSlotIndex: 7, priority: 0)],
            progress,
            CancellationToken.None);

        using var reader = new global::Terrain.TerrainFileReader(outputPath);
        TestHarness.AssertEqual(8, reader.Header.Version, "exported terrain version");
        TestHarness.AssertEqual(33, reader.HeightmapHeader.Width, "heightmap width");
        TestHarness.AssertEqual(33, reader.HeightmapHeader.Height, "heightmap height");
        TestHarness.AssertEqual(2, reader.HeightmapHeader.BytesPerPixel, "heightmap bytes per pixel");

        TestHarness.AssertEqual(17, reader.DetailIndexMapHeader.Width, "detail index width");
        TestHarness.AssertEqual(17, reader.DetailIndexMapHeader.Height, "detail index height");
        TestHarness.AssertEqual(4, reader.DetailIndexMapHeader.BytesPerPixel, "detail index bytes per pixel");
        TestHarness.AssertEqual(17, reader.DetailWeightMapHeader.Width, "detail weight width");
        TestHarness.AssertEqual(17, reader.DetailWeightMapHeader.Height, "detail weight height");
        TestHarness.AssertEqual(4, reader.DetailWeightMapHeader.BytesPerPixel, "detail weight bytes per pixel");
        TestHarness.AssertEqual(2, reader.DetailMapResolutionRatio, "detail map resolution ratio");
        TestHarness.AssertEqual(reader.DetailIndexMapHeader.Mipmaps, reader.DetailWeightMapHeader.Mipmaps, "detail mip count should match");

        byte[] heightPage = new byte[ComputeTileByteSize(reader.HeightmapHeader)];
        byte[] detailIndexPage = new byte[ComputeTileByteSize(reader.DetailIndexMapHeader)];
        byte[] detailWeightPage = new byte[ComputeTileByteSize(reader.DetailWeightMapHeader)];
        var pageKey = new global::Terrain.TerrainPageKey(0, 0, 0);
        reader.ReadHeightPage(pageKey, heightPage);
        reader.ReadDetailIndexPage(pageKey, detailIndexPage);
        reader.ReadDetailWeightPage(pageKey, detailWeightPage);

        int heightSampleOffset = reader.HeightmapHeader.Padding * (reader.HeightmapHeader.TileSize + reader.HeightmapHeader.Padding * 2) + reader.HeightmapHeader.Padding;
        ushort firstHeight = MemoryMarshal.Cast<byte, ushort>(heightPage)[heightSampleOffset];
        TestHarness.AssertEqual(heightData[0], firstHeight, "first height page sample");

        int detailSampleOffset = reader.DetailIndexMapHeader.Padding * (reader.DetailIndexMapHeader.TileSize + reader.DetailIndexMapHeader.Padding * 2) + reader.DetailIndexMapHeader.Padding;
        global::Terrain.Editor.Services.Export.DetailControlPixel firstIndex =
            MemoryMarshal.Cast<byte, global::Terrain.Editor.Services.Export.DetailControlPixel>(detailIndexPage)[detailSampleOffset];
        global::Terrain.Editor.Services.Export.DetailControlPixel firstWeight =
            MemoryMarshal.Cast<byte, global::Terrain.Editor.Services.Export.DetailControlPixel>(detailWeightPage)[detailSampleOffset];
        TestHarness.AssertEqual(7, firstIndex.R, "first baked detail material index");
        TestHarness.AssertEqual(byte.MaxValue, firstWeight.R, "first baked detail material weight");

        long expectedLength = ComputeExpectedTerrainFileLength(reader);
        TestHarness.AssertEqual(expectedLength, new FileInfo(outputPath).Length, "exported terrain should contain exactly height, detail index, and detail weight payloads");
        TestHarness.Assert(progress.Messages.Any(static message => message.Contains("Baking DetailTexture", StringComparison.Ordinal)), "export progress should report detail baking");
        TestHarness.Assert(progress.Messages.Any(static message => message.Contains("Writing DetailIndex VT data", StringComparison.Ordinal)), "export progress should report detail index write");
        TestHarness.Assert(progress.Messages.Any(static message => message.Contains("Writing DetailWeight VT data", StringComparison.Ordinal)), "export progress should report detail weight write");
    }

    private static void TerrainExporterHeightMipsPreserveAlignedSourceSamples()
    {
        ushort[] source =
        [
            0, 1, 2, 3, 4,
            100, 101, 102, 103, 104,
            200, 201, 202, 203, 204,
            300, 301, 302, 303, 304,
            400, 401, 402, 403, 404,
        ];

        ushort[] mip1 = global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.GenerateAlignedHeightMip(source, 5, 5);
        ushort[] mip2 = global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.GenerateAlignedHeightMip(mip1, 3, 3);

        TestHarness.AssertEqual(9, mip1.Length, "mip1 texel count");
        TestHarness.AssertEqual(source[0], mip1[0], "mip1 origin should preserve source sample");
        TestHarness.AssertEqual(source[2], mip1[1], "mip1 x-aligned sample should preserve source sample");
        TestHarness.AssertEqual(source[2 * 5], mip1[3], "mip1 y-aligned sample should preserve source sample");
        TestHarness.AssertEqual(source[2 * 5 + 2], mip1[4], "mip1 interior aligned sample should preserve source sample");
        TestHarness.AssertEqual(source[4 * 5 + 4], mip1[8], "mip1 far edge should preserve source sample");

        TestHarness.AssertEqual(4, mip2.Length, "mip2 texel count");
        TestHarness.AssertEqual(source[0], mip2[0], "mip2 origin should preserve source sample");
        TestHarness.AssertEqual(source[4], mip2[1], "mip2 x edge should preserve source sample");
        TestHarness.AssertEqual(source[4 * 5], mip2[2], "mip2 y edge should preserve source sample");
        TestHarness.AssertEqual(source[4 * 5 + 4], mip2[3], "mip2 far edge should preserve source sample");
    }

    private static void TerrainExporterWritesAlignedHeightMipsIntoTerrainPayload()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-exporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "terrain.terrain");

        const int heightSize = 513;
        const int detailSize = 257;
        ushort[] heightData = new ushort[heightSize * heightSize];
        for (int y = 0; y < heightSize; y++)
        {
            for (int x = 0; x < heightSize; x++)
            {
                heightData[y * heightSize + x] = EncodeHeightSampleForMipTest(x, y);
            }
        }

        byte[] biomeMask = new byte[detailSize * detailSize];

        global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportBakedTerrain(
            outputPath,
            heightData,
            heightSize,
            heightSize,
            100.0f,
            biomeMask,
            detailSize,
            detailSize,
            [CreateLayer(0, materialSlotIndex: 7, priority: 0)],
            progress: null,
            CancellationToken.None);

        using var reader = new global::Terrain.TerrainFileReader(outputPath);
        TestHarness.Assert(reader.HeightmapHeader.Mipmaps >= 3, "test terrain should produce height mip2");

        byte[] mip1Page = new byte[ComputeTileByteSize(reader.HeightmapHeader)];
        byte[] mip2Page = new byte[ComputeTileByteSize(reader.HeightmapHeader)];
        reader.ReadHeightPage(new global::Terrain.TerrainPageKey(1, 0, 0), mip1Page);
        reader.ReadHeightPage(new global::Terrain.TerrainPageKey(2, 0, 0), mip2Page);

        TestHarness.AssertEqual(EncodeHeightSampleForMipTest(2, 2), ReadHeightPageSample(reader.HeightmapHeader, mip1Page, 1, 1), "mip1 interior payload sample");
        TestHarness.AssertEqual(EncodeHeightSampleForMipTest(256, 0), ReadHeightPageSample(reader.HeightmapHeader, mip1Page, 128, 0), "mip1 x edge payload sample");
        TestHarness.AssertEqual(EncodeHeightSampleForMipTest(4, 4), ReadHeightPageSample(reader.HeightmapHeader, mip2Page, 1, 1), "mip2 interior payload sample");
        TestHarness.AssertEqual(EncodeHeightSampleForMipTest(512, 512), ReadHeightPageSample(reader.HeightmapHeader, mip2Page, 128, 128), "mip2 far edge payload sample");
    }

    private static void TerrainExporterSupportsEvenHeightmapDimensionsWithAlignedMips()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-exporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "terrain-even.terrain");

        const int heightSize = 512;
        const int detailSize = 256;
        ushort[] heightData = new ushort[heightSize * heightSize];
        for (int y = 0; y < heightSize; y++)
        {
            for (int x = 0; x < heightSize; x++)
            {
                heightData[y * heightSize + x] = EncodeHeightSampleForMipTest(x, y);
            }
        }

        byte[] biomeMask = new byte[detailSize * detailSize];

        global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportBakedTerrain(
            outputPath,
            heightData,
            heightSize,
            heightSize,
            100.0f,
            biomeMask,
            detailSize,
            detailSize,
            [CreateLayer(0, materialSlotIndex: 7, priority: 0)],
            progress: null,
            CancellationToken.None);

        using var reader = new global::Terrain.TerrainFileReader(outputPath);
        TestHarness.AssertEqual(heightSize, reader.HeightmapHeader.Width, "even heightmap width should be preserved");
        TestHarness.AssertEqual(heightSize, reader.HeightmapHeader.Height, "even heightmap height should be preserved");

        byte[] mip1Page = new byte[ComputeTileByteSize(reader.HeightmapHeader)];
        byte[] mip2Page = new byte[ComputeTileByteSize(reader.HeightmapHeader)];
        reader.ReadHeightPage(new global::Terrain.TerrainPageKey(1, 0, 0), mip1Page);
        reader.ReadHeightPage(new global::Terrain.TerrainPageKey(2, 0, 0), mip2Page);

        TestHarness.AssertEqual(EncodeHeightSampleForMipTest(2, 2), ReadHeightPageSample(reader.HeightmapHeader, mip1Page, 1, 1), "even mip1 interior payload sample");
        TestHarness.AssertEqual(EncodeHeightSampleForMipTest(254, 254), ReadHeightPageSample(reader.HeightmapHeader, mip1Page, 127, 127), "even mip1 represented edge sample");
        TestHarness.AssertEqual(EncodeHeightSampleForMipTest(4, 4), ReadHeightPageSample(reader.HeightmapHeader, mip2Page, 1, 1), "even mip2 interior payload sample");
        TestHarness.AssertEqual(EncodeHeightSampleForMipTest(508, 508), ReadHeightPageSample(reader.HeightmapHeader, mip2Page, 127, 127), "even mip2 represented edge sample");
    }

    private static void TerrainExporterAggregatesDetailMipContributionsInsteadOfCopyingTopLeftTexel()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-exporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "terrain.terrain");

        const int heightSize = 1025;
        const int detailSize = 513;
        ushort[] heightData = new ushort[heightSize * heightSize];
        byte[] biomeMask = new byte[detailSize * detailSize];
        biomeMask[0] = 0;
        biomeMask[1] = 1;
        biomeMask[detailSize] = 2;
        biomeMask[detailSize + 1] = 3;

        global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportBakedTerrain(
            outputPath,
            heightData,
            heightSize,
            heightSize,
            100.0f,
            biomeMask,
            detailSize,
            detailSize,
            [
                CreateLayer(0, materialSlotIndex: 1, priority: 0),
                CreateLayer(1, materialSlotIndex: 2, priority: 1),
                CreateLayer(2, materialSlotIndex: 1, priority: 2),
                CreateLayer(3, materialSlotIndex: 3, priority: 3),
            ],
            progress: null,
            CancellationToken.None);

        using var reader = new global::Terrain.TerrainFileReader(outputPath);
        TestHarness.Assert(reader.DetailMapMipCount >= 2, "test terrain should produce detail mip1");

        byte[] mip1IndexPage = new byte[ComputeTileByteSize(reader.DetailIndexMapHeader)];
        byte[] mip1WeightPage = new byte[ComputeTileByteSize(reader.DetailWeightMapHeader)];
        var mip1Key = new global::Terrain.TerrainPageKey(1, 0, 0);
        reader.ReadDetailIndexPage(mip1Key, mip1IndexPage);
        reader.ReadDetailWeightPage(mip1Key, mip1WeightPage);

        int sampleOffset = reader.DetailIndexMapHeader.Padding * (reader.DetailIndexMapHeader.TileSize + reader.DetailIndexMapHeader.Padding * 2) + reader.DetailIndexMapHeader.Padding;
        global::Terrain.Editor.Services.Export.DetailControlPixel index =
            MemoryMarshal.Cast<byte, global::Terrain.Editor.Services.Export.DetailControlPixel>(mip1IndexPage)[sampleOffset];
        global::Terrain.Editor.Services.Export.DetailControlPixel weight =
            MemoryMarshal.Cast<byte, global::Terrain.Editor.Services.Export.DetailControlPixel>(mip1WeightPage)[sampleOffset];

        TestHarness.AssertEqual(1, index.R, "aggregated dominant material should merge the two material 1 source texels");
        TestHarness.AssertEqual(2, index.G, "second material should survive detail mip aggregation");
        TestHarness.AssertEqual(3, index.B, "third material should survive detail mip aggregation");
        TestHarness.AssertEqual(byte.MaxValue, index.A, "unused detail mip material channel");
        TestHarness.AssertEqual(128, weight.R, "merged material weight should be normalized from two source texels");
        TestHarness.AssertEqual(64, weight.G, "second material weight should be normalized from one source texel");
        TestHarness.AssertEqual(64, weight.B, "third material weight should be normalized from one source texel");
        TestHarness.AssertEqual(0, weight.A, "unused detail mip weight channel");
    }

    private static void TerrainExporterAggregatesMultiChannelDetailMipTopFour()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-exporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "terrain.terrain");

        const int heightSize = 513;
        const int detailSize = 257;
        ushort[] heightData = new ushort[heightSize * heightSize];
        byte[] biomeMask = new byte[detailSize * detailSize];
        biomeMask[0] = 0;
        biomeMask[1] = 1;
        biomeMask[detailSize] = 2;
        biomeMask[detailSize + 1] = 3;

        global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportBakedTerrain(
            outputPath,
            heightData,
            heightSize,
            heightSize,
            100.0f,
            biomeMask,
            detailSize,
            detailSize,
            [
                CreateLayer(0, materialSlotIndex: 2, priority: 0),
                CreateLayer(0, materialSlotIndex: 1, priority: 1, CreateWeightModifier(0.5f)),
                CreateLayer(1, materialSlotIndex: 4, priority: 0),
                CreateLayer(1, materialSlotIndex: 3, priority: 1, CreateWeightModifier(0.5f)),
                CreateLayer(2, materialSlotIndex: 6, priority: 0),
                CreateLayer(2, materialSlotIndex: 5, priority: 1, CreateWeightModifier(0.5f)),
                CreateLayer(3, materialSlotIndex: 8, priority: 0),
                CreateLayer(3, materialSlotIndex: 7, priority: 1, CreateWeightModifier(0.5f)),
            ],
            progress: null,
            CancellationToken.None);

        using var reader = new global::Terrain.TerrainFileReader(outputPath);
        byte[] mip1IndexPage = new byte[ComputeTileByteSize(reader.DetailIndexMapHeader)];
        byte[] mip1WeightPage = new byte[ComputeTileByteSize(reader.DetailWeightMapHeader)];
        reader.ReadDetailIndexPage(new global::Terrain.TerrainPageKey(1, 0, 0), mip1IndexPage);
        reader.ReadDetailWeightPage(new global::Terrain.TerrainPageKey(1, 0, 0), mip1WeightPage);

        int sampleOffset = reader.DetailIndexMapHeader.Padding * (reader.DetailIndexMapHeader.TileSize + reader.DetailIndexMapHeader.Padding * 2) + reader.DetailIndexMapHeader.Padding;
        global::Terrain.Editor.Services.Export.DetailControlPixel index =
            MemoryMarshal.Cast<byte, global::Terrain.Editor.Services.Export.DetailControlPixel>(mip1IndexPage)[sampleOffset];
        global::Terrain.Editor.Services.Export.DetailControlPixel weight =
            MemoryMarshal.Cast<byte, global::Terrain.Editor.Services.Export.DetailControlPixel>(mip1WeightPage)[sampleOffset];

        TestHarness.AssertEqual(1, index.R, "first top-four material should be the lowest tied material id");
        TestHarness.AssertEqual(2, index.G, "second top-four material should preserve the second tied material id");
        TestHarness.AssertEqual(3, index.B, "third top-four material should preserve the third tied material id");
        TestHarness.AssertEqual(4, index.A, "fourth top-four material should preserve the fourth tied material id");
        TestHarness.AssertEqual(64, weight.R, "first selected material should be renormalized after dropping lower-ranked contributions");
        TestHarness.AssertEqual(64, weight.G, "second selected material should be renormalized after dropping lower-ranked contributions");
        TestHarness.AssertEqual(64, weight.B, "third selected material should be renormalized after dropping lower-ranked contributions");
        TestHarness.AssertEqual(64, weight.A, "fourth selected material should be renormalized after dropping lower-ranked contributions");
    }

    private static void TerrainExporterSortsEqualWeightDetailMipContributionsByMaterialIndex()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-exporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "terrain.terrain");

        const int heightSize = 513;
        const int detailSize = 257;
        ushort[] heightData = new ushort[heightSize * heightSize];
        byte[] biomeMask = new byte[detailSize * detailSize];
        biomeMask[0] = 0;
        biomeMask[1] = 1;
        biomeMask[detailSize] = 2;
        biomeMask[detailSize + 1] = 3;

        global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportBakedTerrain(
            outputPath,
            heightData,
            heightSize,
            heightSize,
            100.0f,
            biomeMask,
            detailSize,
            detailSize,
            [
                CreateLayer(0, materialSlotIndex: 9, priority: 0),
                CreateLayer(1, materialSlotIndex: 2, priority: 1),
                CreateLayer(2, materialSlotIndex: 7, priority: 2),
                CreateLayer(3, materialSlotIndex: 4, priority: 3),
            ],
            progress: null,
            CancellationToken.None);

        using var reader = new global::Terrain.TerrainFileReader(outputPath);
        byte[] mip1IndexPage = new byte[ComputeTileByteSize(reader.DetailIndexMapHeader)];
        byte[] mip1WeightPage = new byte[ComputeTileByteSize(reader.DetailWeightMapHeader)];
        reader.ReadDetailIndexPage(new global::Terrain.TerrainPageKey(1, 0, 0), mip1IndexPage);
        reader.ReadDetailWeightPage(new global::Terrain.TerrainPageKey(1, 0, 0), mip1WeightPage);

        int sampleOffset = reader.DetailIndexMapHeader.Padding * (reader.DetailIndexMapHeader.TileSize + reader.DetailIndexMapHeader.Padding * 2) + reader.DetailIndexMapHeader.Padding;
        global::Terrain.Editor.Services.Export.DetailControlPixel index =
            MemoryMarshal.Cast<byte, global::Terrain.Editor.Services.Export.DetailControlPixel>(mip1IndexPage)[sampleOffset];
        global::Terrain.Editor.Services.Export.DetailControlPixel weight =
            MemoryMarshal.Cast<byte, global::Terrain.Editor.Services.Export.DetailControlPixel>(mip1WeightPage)[sampleOffset];

        TestHarness.AssertEqual(2, index.R, "lowest material index should sort first when mip weights tie");
        TestHarness.AssertEqual(4, index.G, "second lowest material index should sort second when mip weights tie");
        TestHarness.AssertEqual(7, index.B, "third lowest material index should sort third when mip weights tie");
        TestHarness.AssertEqual(9, index.A, "highest material index should sort fourth when mip weights tie");
        TestHarness.AssertEqual(64, weight.R, "first equal mip weight");
        TestHarness.AssertEqual(64, weight.G, "second equal mip weight");
        TestHarness.AssertEqual(64, weight.B, "third equal mip weight");
        TestHarness.AssertEqual(64, weight.A, "fourth equal mip weight");
    }

    private static void TerrainExporterCancellationPreservesExistingTargetAndDeletesTempFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-v8-exporter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "terrain.terrain");

        ushort[] originalHeightData = new ushort[33 * 33];
        originalHeightData[0] = 1234;
        byte[] biomeMask = Enumerable.Repeat((byte)0, 17 * 17).ToArray();
        global::Terrain.Editor.Services.BiomeRuleLayer[] layers = [CreateLayer(0, materialSlotIndex: 7, priority: 0)];
        global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportBakedTerrain(
            outputPath,
            originalHeightData,
            33,
            33,
            100.0f,
            biomeMask,
            17,
            17,
            layers,
            progress: null,
            CancellationToken.None);

        long originalLength = new FileInfo(outputPath).Length;
        using (var originalReader = new global::Terrain.TerrainFileReader(outputPath))
        {
            TestHarness.AssertEqual((ushort)1234, ReadFirstHeightSample(originalReader), "original exported height sample");
        }

        ushort[] replacementHeightData = new ushort[33 * 33];
        replacementHeightData[0] = 5678;
        using var cts = new CancellationTokenSource();
        var cancelOnWrite = new CancelOnMessageProgress(cts, "Writing HeightMap VT data");

        TestHarness.AssertThrows<OperationCanceledException>(
            () => global::Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportBakedTerrain(
                outputPath,
                replacementHeightData,
                33,
                33,
                100.0f,
                biomeMask,
                17,
                17,
                layers,
                cancelOnWrite,
                cts.Token),
            "cancelled export should throw");

        TestHarness.AssertEqual(originalLength, new FileInfo(outputPath).Length, "cancelled export should preserve existing target length");
        using (var reader = new global::Terrain.TerrainFileReader(outputPath))
        {
            TestHarness.AssertEqual((ushort)1234, ReadFirstHeightSample(reader), "cancelled export should preserve existing target content");
        }

        string[] tempFiles = Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly);
        TestHarness.AssertEqual(0, tempFiles.Length, "cancelled export should delete temp files");
    }

    private static void TerrainExporterSourceWritesDetailIndexAndWeightVtPayloads()
    {
        string exporterSource = ReadRepoText("Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs");
        TestHarness.Assert(exporterSource.Contains("BakedDetailMapBuilder.Generate", StringComparison.Ordinal), "exporter should bake detail maps");
        TestHarness.Assert(exporterSource.Contains("Writing DetailIndex VT data", StringComparison.Ordinal), "export progress should mention detail index");
        TestHarness.Assert(exporterSource.Contains("Writing DetailWeight VT data", StringComparison.Ordinal), "export progress should mention detail weight");
        TestHarness.Assert(exporterSource.Contains("BytesPerPixel = 4", StringComparison.Ordinal), "detail VT payloads should be RGBA8");
        TestHarness.Assert(exporterSource.Contains("StreamMipLevels<DetailControlPixel>", StringComparison.Ordinal), "detail VT streams should use packed DetailControlPixel");
        TestHarness.Assert(!exporterSource.Contains("StreamMipLevels<byte>", StringComparison.Ordinal), "detail VT streams should not use byte streams");
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

    private static ushort EncodeHeightSampleForMipTest(int x, int y)
    {
        return checked((ushort)(y * 100 + x));
    }

    private static ushort ReadHeightPageSample(global::Terrain.TerrainVirtualTextureHeader header, byte[] page, int x, int y)
    {
        int paddedTileSize = checked(header.TileSize + header.Padding * 2);
        int offset = checked((y + header.Padding) * paddedTileSize + x + header.Padding);
        return MemoryMarshal.Cast<byte, ushort>(page)[offset];
    }

    private static long ComputeExpectedTerrainFileLength(global::Terrain.TerrainFileReader reader)
    {
        long length = Marshal.SizeOf<global::Terrain.Editor.Models.TerrainFileHeader>() + sizeof(int);
        foreach (global::Terrain.TerrainMinMaxErrorMap map in reader.ReadAllMinMaxErrorMaps())
            length += sizeof(int) + sizeof(int) + (long)map.Width * map.Height * 3 * sizeof(float);

        length += Marshal.SizeOf<global::Terrain.Editor.Models.VTHeader>() + ComputePayloadByteLength(reader.HeightmapHeader);
        length += Marshal.SizeOf<global::Terrain.Editor.Models.VTHeader>() + ComputePayloadByteLength(reader.DetailIndexMapHeader);
        length += Marshal.SizeOf<global::Terrain.Editor.Models.VTHeader>() + ComputePayloadByteLength(reader.DetailWeightMapHeader);
        return length;
    }

    private static long ComputePayloadByteLength(global::Terrain.TerrainVirtualTextureHeader header)
    {
        long pageBytes = ComputeTileByteSize(header);
        long total = 0;
        for (int mip = 0; mip < header.Mipmaps; mip++)
        {
            global::Terrain.VirtualTextureMipLayoutInfo layout =
                global::Terrain.VirtualTextureLayout.GetMipLayout(header.Width, header.Height, header.TileSize, mip);
            total += (long)layout.TilesX * layout.TilesY * pageBytes;
        }

        return total;
    }

    private static ushort ReadFirstHeightSample(global::Terrain.TerrainFileReader reader)
    {
        byte[] heightPage = new byte[ComputeTileByteSize(reader.HeightmapHeader)];
        reader.ReadHeightPage(new global::Terrain.TerrainPageKey(0, 0, 0), heightPage);
        int sampleOffset = reader.HeightmapHeader.Padding * (reader.HeightmapHeader.TileSize + reader.HeightmapHeader.Padding * 2) + reader.HeightmapHeader.Padding;
        return MemoryMarshal.Cast<byte, ushort>(heightPage)[sampleOffset];
    }

    private sealed class RecordingExportProgress : IProgress<global::Terrain.Editor.Services.Export.ExportProgress>
    {
        public List<string> Messages { get; } = new();

        public void Report(global::Terrain.Editor.Services.Export.ExportProgress value)
        {
            if (!string.IsNullOrWhiteSpace(value.Message))
                Messages.Add(value.Message);
        }
    }

    private sealed class CancelOnMessageProgress(CancellationTokenSource cancellation, string messagePart) : IProgress<global::Terrain.Editor.Services.Export.ExportProgress>
    {
        public void Report(global::Terrain.Editor.Services.Export.ExportProgress value)
        {
            if (value.Message.Contains(messagePart, StringComparison.Ordinal))
                cancellation.Cancel();
        }
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
