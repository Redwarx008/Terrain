using Terrain;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class TerrainRuntimeLoadBehaviorTests
{
    public static void RunAll()
    {
        TestHarness.Run("runtime load gate blocks repeated retries until config changes", RuntimeLoadGateBlocksRepeatedRetriesUntilConfigChanges);
        TestHarness.Run("terrain runtime load disposes reader when detail map build fails", TerrainRuntimeLoadDisposesReaderWhenDetailMapBuildFails);
        TestHarness.Run("runtime load failure marks component when terrain data is missing", RuntimeLoadFailureMarksComponentWhenTerrainDataIsMissing);
        TestHarness.Run("runtime load failure marks component when biome mask is missing", RuntimeLoadFailureMarksComponentWhenBiomeMaskIsMissing);
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
                static (_, _) => throw new InvalidDataException("detail map build failed")),
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

    private static TerrainRuntimeResourceBundle CreateResourceBundle()
    {
        return new TerrainRuntimeResourceBundle
        {
            TerrainDataPath = "fake.terrain",
            HeightScale = 123.0f,
        };
    }

}
