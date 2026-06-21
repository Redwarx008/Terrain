using Terrain;
using Terrain.Resources;
using Terrain.Rivers;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class TerrainRuntimeLoadBehaviorTests
{
    public static void RunAll()
    {
        TestHarness.Run("runtime load gate blocks repeated retries until config changes", RuntimeLoadGateBlocksRepeatedRetriesUntilConfigChanges);
        TestHarness.Run("terrain runtime load disposes reader when detail map build fails", TerrainRuntimeLoadDisposesReaderWhenDetailMapBuildFails);
        TestHarness.Run("runtime load failure marks component when terrain data is missing", RuntimeLoadFailureMarksComponentWhenTerrainDataIsMissing);
        TestHarness.Run("runtime load failure marks component when biome mask is missing", RuntimeLoadFailureMarksComponentWhenBiomeMaskIsMissing);
        TestHarness.Run("terrain height sampler reads requested height tile on demand", TerrainHeightSamplerReadsRequestedHeightTileOnDemand);
        TestHarness.Run("terrain height sampler caches at most four tiles", TerrainHeightSamplerCachesAtMostFourTiles);
        TestHarness.Run("terrain component does not retain full runtime height data", TerrainComponentDoesNotRetainFullRuntimeHeightData);
        TestHarness.Run("runtime detail map uses terrain component height interface", RuntimeDetailMapUsesTerrainComponentHeightInterface);
    }

    private static void RuntimeLoadGateBlocksRepeatedRetriesUntilConfigChanges()
    {
        var component = new TerrainComponent();

        TestHarness.Assert(TerrainProcessor.ShouldAttemptRuntimeLoad(component), "fresh component should attempt runtime load");

        TerrainProcessor.MarkRuntimeLoadFailure(component);

        TestHarness.Assert(!TerrainProcessor.ShouldAttemptRuntimeLoad(component), "same config should not retry after a failed runtime load");

        component.MaxResidentChunks += 1;

        TestHarness.Assert(TerrainProcessor.ShouldAttemptRuntimeLoad(component), "config changes should re-enable runtime load attempts");

        TerrainProcessor.MarkRuntimeLoadSuccess(component);

        TestHarness.Assert(TerrainProcessor.ShouldAttemptRuntimeLoad(component), "successful load should clear runtime failure gate");
    }

    private static void TerrainRuntimeLoadDisposesReaderWhenDetailMapBuildFails()
    {
        var component = new TerrainComponent();
        var reader = new FakeTerrainFileReader();

        TestHarness.AssertThrows<InvalidDataException>(
            () => TerrainProcessor.CreateLoadedTerrainData(
                component,
                CreateResourceBundle(),
                _ => reader,
                static (_, _, _) => throw new InvalidDataException("detail map build failed")),
            "detail map build failure should surface as InvalidDataException");

        TestHarness.Assert(reader.IsDisposed, "reader should be disposed when load fails after opening terrain data");
    }

    private static void RuntimeLoadFailureMarksComponentWhenTerrainDataIsMissing()
    {
        var component = new TerrainComponent();
        var diagnostics = new List<string>();

        bool loaded = TerrainProcessor.TryLoadRuntimeData(
            component,
            CreateResourceBundle,
            out _,
            _ => throw new FileNotFoundException("terrain data missing", "map/terrain.terrain"),
            logError: diagnostics.Add);

        TestHarness.Assert(!loaded, "missing terrain data should fail the runtime load");
        TestHarness.Assert(!component.IsInitialized, "failed runtime load should keep the component uninitialized");
        TestHarness.Assert(component.HasRuntimeLoadFailure, "failed runtime load should latch the failure gate");
        TestHarness.Assert(!TerrainProcessor.ShouldAttemptRuntimeLoad(component), "same config should not retry after a real runtime load failure");
        TestHarness.Assert(
            diagnostics.Any(message => message.Contains("map/terrain.terrain", StringComparison.Ordinal)),
            "runtime load error should mention the missing terrain data path");
    }

    private static void RuntimeLoadFailureMarksComponentWhenBiomeMaskIsMissing()
    {
        var component = new TerrainComponent();
        var diagnostics = new List<string>();
        var reader = new FakeTerrainFileReader();
        string missingBiomeMaskPath = Path.Combine(Path.GetTempPath(), "terrain-runtime-load-tests", Guid.NewGuid().ToString("N"), "biome_mask.png");

        bool loaded = TerrainProcessor.TryLoadRuntimeData(
            component,
            () => new TerrainRuntimeResourceBundle
            {
                TerrainDataPath = "fake.terrain",
                BiomeMaskPath = missingBiomeMaskPath,
                HeightScale = 123.0f,
            },
            out _,
            _ => reader,
            logError: diagnostics.Add);

        TestHarness.Assert(!loaded, "missing biome mask should fail the runtime load");
        TestHarness.Assert(!component.IsInitialized, "failed biome mask load should keep the component uninitialized");
        TestHarness.Assert(component.HasRuntimeLoadFailure, "failed biome mask load should latch the failure gate");
        TestHarness.Assert(!TerrainProcessor.ShouldAttemptRuntimeLoad(component), "same config should not retry after a biome mask load failure");
        TestHarness.Assert(reader.IsDisposed, "reader should be disposed when runtime detail map generation fails");
        TestHarness.Assert(
            diagnostics.Any(message => message.Contains("biome_mask", StringComparison.OrdinalIgnoreCase)),
            "runtime load error should mention the missing biome mask");
    }

    private static void TerrainHeightSamplerReadsRequestedHeightTileOnDemand()
    {
        var reader = new FakeTerrainFileReader(
            width: 4,
            height: 4,
            heightData:
            [
                0, 1000, 2000, 3000,
                4000, 5000, 6000, 7000,
                8000, 9000, 10000, 11000,
                12000, 13000, 14000, 15000,
            ]);

        var sampler = new TerrainHeightSampler(reader);

        float sample = sampler.GetHeight(1, 1, 123.0f);

        TestHarness.AssertEqual(5000.0f * (1.0f / ushort.MaxValue) * 123.0f, sample, "height sampler should return the requested discrete sample height");
        TestHarness.AssertEqual(1, reader.HeightPageReadCount, "height sampler should read the requested tile on demand");

        _ = sampler.GetHeight(1, 1, 123.0f);

        TestHarness.AssertEqual(1, reader.HeightPageReadCount, "height sampler should reuse cached height tiles");
    }

    private static void TerrainHeightSamplerCachesAtMostFourTiles()
    {
        var reader = new FakeTerrainFileReader(width: 11, height: 3, tileSize: 3);
        var sampler = new TerrainHeightSampler(reader);

        _ = sampler.GetHeight(0, 0, 123.0f);
        _ = sampler.GetHeight(2, 0, 123.0f);
        _ = sampler.GetHeight(4, 0, 123.0f);
        _ = sampler.GetHeight(6, 0, 123.0f);
        _ = sampler.GetHeight(8, 0, 123.0f);
        _ = sampler.GetHeight(0, 0, 123.0f);

        TestHarness.AssertEqual(6, reader.HeightPageReadCount, "height sampler should evict the least recently used page after four cached tiles");
    }

    private static void TerrainComponentDoesNotRetainFullRuntimeHeightData()
    {
        var component = new TerrainComponent();
        var reader = new FakeTerrainFileReader();

        bool loaded = TerrainProcessor.TryLoadRuntimeData(
            component,
            CreateResourceBundle,
            out _,
            _ => reader,
            static (_, _, _) => new RuntimeDetailMapData(new byte[4], new byte[4], 2, 2));

        TestHarness.Assert(loaded, "runtime terrain data should load");
        TestHarness.AssertEqual(0, reader.ReadAllHeightDataCount, "runtime load should not read the full heightmap into CPU memory");
        string componentSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Terrain", "Core", "TerrainComponent.cs"));
        TestHarness.Assert(!componentSource.Contains("RuntimeHeightData", StringComparison.Ordinal), "TerrainComponent should not retain full runtime height data");
    }

    private static void RuntimeDetailMapUsesTerrainComponentHeightInterface()
    {
        var component = new TerrainComponent();
        var reader = new FakeTerrainFileReader(
            width: 4,
            height: 4,
            heightData:
            [
                0, 1000, 2000, 3000,
                4000, 5000, 6000, 7000,
                8000, 9000, 10000, 11000,
                12000, 13000, 14000, 15000,
            ]);

        bool loaded = TerrainProcessor.TryLoadRuntimeData(
            component,
            CreateResourceBundle,
            out _,
            _ => reader,
            static (terrain, _, _) =>
            {
                float sample = terrain.GetHeight(1, 1);
                TestHarness.AssertEqual(5000.0f * (1.0f / ushort.MaxValue) * 123.0f, sample, "runtime detail map builder should sample through TerrainComponent.GetHeight");
                return new RuntimeDetailMapData(new byte[4], new byte[4], 2, 2);
            });

        TestHarness.Assert(loaded, "runtime terrain data should load");
        TestHarness.AssertEqual(1, reader.HeightPageReadCount, "runtime detail map builder should trigger only requested height tile load");
        TestHarness.AssertEqual(0, reader.ReadAllHeightDataCount, "runtime detail map builder should not read the full heightmap");
    }

    private static string FindRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "Terrain.sln")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from AppContext.BaseDirectory.");
    }

    private static TerrainRuntimeResourceBundle CreateResourceBundle()
    {
        return new TerrainRuntimeResourceBundle
        {
            TerrainDataPath = "fake.terrain",
            HeightScale = 123.0f,
        };
    }

}
