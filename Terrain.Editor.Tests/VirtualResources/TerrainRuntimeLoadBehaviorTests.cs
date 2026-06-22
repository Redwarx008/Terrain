using Terrain;
using Terrain.Resources;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class TerrainRuntimeLoadBehaviorTests
{
    public static void RunAll()
    {
        TestHarness.Run("runtime load gate blocks repeated retries until config changes", RuntimeLoadGateBlocksRepeatedRetriesUntilConfigChanges);
        TestHarness.Run("runtime load failure marks component when terrain data is missing", RuntimeLoadFailureMarksComponentWhenTerrainDataIsMissing);
        TestHarness.Run("terrain component get height fails before streaming attaches", TerrainComponentGetHeightFailsBeforeStreamingAttaches);
        TestHarness.Run("runtime apply failure is captured by runtime load gate", RuntimeApplyFailureIsCapturedByRuntimeLoadGate);
        TestHarness.Run("runtime height sampling stays inside terrain streaming", RuntimeHeightSamplingStaysInsideTerrainStreaming);
        TestHarness.Run("runtime height CPU cache follows terrain streaming residency", RuntimeHeightCpuCacheFollowsTerrainStreamingResidency);
        TestHarness.Run("runtime terrain startup does not build detail maps", RuntimeTerrainStartupDoesNotBuildDetailMaps);
        TestHarness.Run("terrain component does not retain full runtime height data", TerrainComponentDoesNotRetainFullRuntimeHeightData);
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

    private static void TerrainComponentGetHeightFailsBeforeStreamingAttaches()
    {
        var component = new TerrainComponent();

        TestHarness.AssertThrows<InvalidOperationException>(
            () => component.GetHeight(0, 0),
            "TerrainComponent.GetHeight should fail before terrain streaming is initialized");
    }

    private static void RuntimeApplyFailureIsCapturedByRuntimeLoadGate()
    {
        string terrainProcessorSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Terrain", "Core", "TerrainProcessor.cs"));
        int applyCallIndex = terrainProcessorSource.IndexOf("ApplyLoadedTerrainData(graphicsDevice", StringComparison.Ordinal);
        int applyFailureHandlerIndex = terrainProcessorSource.IndexOf("HandleRuntimeApplyFailure(component, renderObject, exception)", StringComparison.Ordinal);
        int markSuccessIndex = terrainProcessorSource.IndexOf("MarkRuntimeLoadSuccess(component);", applyCallIndex, StringComparison.Ordinal);
        int tryLoadRuntimeDataIndex = terrainProcessorSource.IndexOf("internal static bool TryLoadRuntimeData", StringComparison.Ordinal);
        int createLoadedTerrainDataMethodIndex = terrainProcessorSource.IndexOf("internal static LoadedTerrainData CreateLoadedTerrainData", StringComparison.Ordinal);
        int createLoadedDataIndex = terrainProcessorSource.IndexOf("loadedData = CreateLoadedTerrainData", StringComparison.Ordinal);

        TestHarness.Assert(applyCallIndex >= 0, "runtime load should apply loaded terrain data during initialization");
        TestHarness.Assert(applyFailureHandlerIndex > applyCallIndex, "apply failures should be handled after ApplyLoadedTerrainData throws");
        TestHarness.Assert(markSuccessIndex > applyCallIndex && markSuccessIndex < applyFailureHandlerIndex, "runtime load success should be marked only after ApplyLoadedTerrainData completes");
        TestHarness.Assert(createLoadedDataIndex > tryLoadRuntimeDataIndex, "TryLoadRuntimeData should still create loaded runtime metadata");

        string tryLoadRuntimeDataBody = terrainProcessorSource[tryLoadRuntimeDataIndex..createLoadedTerrainDataMethodIndex];
        TestHarness.Assert(!tryLoadRuntimeDataBody.Contains("MarkRuntimeLoadSuccess(component)", StringComparison.Ordinal), "TryLoadRuntimeData should not mark success before runtime apply succeeds");

        int handlerIndex = terrainProcessorSource.IndexOf("private static void HandleRuntimeApplyFailure", StringComparison.Ordinal);
        TestHarness.Assert(handlerIndex >= 0, "runtime apply failure cleanup should be centralized");
        string handlerBody = terrainProcessorSource[handlerIndex..Math.Min(terrainProcessorSource.Length, handlerIndex + 1200)];
        TestHarness.Assert(handlerBody.Contains("MarkRuntimeLoadFailure(component)", StringComparison.Ordinal), "apply failure should latch the runtime load failure gate");
        TestHarness.Assert(handlerBody.Contains("component.QuadTree?.Dispose()", StringComparison.Ordinal), "apply failure should dispose any partially attached streaming state");
        TestHarness.Assert(handlerBody.Contains("renderObject.Enabled = false", StringComparison.Ordinal), "apply failure should disable the render object");
    }

    private static void RuntimeHeightSamplingStaysInsideTerrainStreaming()
    {
        string repositoryRoot = FindRepositoryRoot();
        string terrainComponentSource = File.ReadAllText(Path.Combine(repositoryRoot, "Terrain", "Core", "TerrainComponent.cs"));
        string terrainStreamingSource = File.ReadAllText(Path.Combine(repositoryRoot, "Terrain", "Streaming", "TerrainStreaming.cs"));

        TestHarness.Assert(!File.Exists(Path.Combine(repositoryRoot, "Terrain", "Streaming", "TerrainHeightSampler.cs")), "runtime height sampling should not be a separate TerrainHeightSampler abstraction");
        TestHarness.Assert(!terrainComponentSource.Contains("TerrainHeightSampler", StringComparison.Ordinal), "TerrainComponent should not store a separate height sampler");
        TestHarness.Assert(terrainStreamingSource.Contains("GetHeight(int sampleX, int sampleZ", StringComparison.Ordinal), "height sampling should live inside terrain streaming");
    }

    private static void RuntimeHeightCpuCacheFollowsTerrainStreamingResidency()
    {
        string terrainStreamingSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Terrain", "Streaming", "TerrainStreaming.cs"));

        TestHarness.Assert(terrainStreamingSource.Contains("maxCachedHeightPages = Math.Max(1, gpuHeightArray.Capacity)", StringComparison.Ordinal), "CPU height cache capacity should follow GPU resident page capacity");
        TestHarness.Assert(terrainStreamingSource.Contains("gpuHeightArray.PageEvicted += RemoveCachedHeightPage", StringComparison.Ordinal), "CPU height cache should release pages when GPU height pages are evicted");
        TestHarness.Assert(terrainStreamingSource.Contains("CacheHeightPage(request.Key, request.Data.Memory.Span, isGpuResident: true)", StringComparison.Ordinal), "uploaded height pages should stay cached on CPU after GPU upload");
        TestHarness.Assert(!terrainStreamingSource.Contains("MaxCachedPages = 4", StringComparison.Ordinal), "CPU height cache should not be a fixed four-page sampler cache");
        TestHarness.Assert(!terrainStreamingSource.Contains("heightCacheGate", StringComparison.Ordinal), "CPU height cache should not block runtime generation behind a global lock");
    }

    private static void RuntimeTerrainStartupDoesNotBuildDetailMaps()
    {
        string repositoryRoot = FindRepositoryRoot();
        string processor = File.ReadAllText(Path.Combine(repositoryRoot, "Terrain", "Core", "TerrainProcessor.cs"));
        string streaming = File.ReadAllText(Path.Combine(repositoryRoot, "Terrain", "Streaming", "TerrainStreaming.cs"));

        TestHarness.Assert(!processor.Contains("RuntimeDetailMapBuilder", StringComparison.Ordinal), "runtime processor should not reference runtime detail builder");
        TestHarness.Assert(!processor.Contains("LoadRuntimeBiomeMask", StringComparison.Ordinal), "runtime processor should not load biome masks for terrain detail");
        TestHarness.Assert(!streaming.Contains("RuntimeDetailMapData", StringComparison.Ordinal), "runtime streaming should not retain full generated detail data");
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
            static (_, _, _, _) => new RuntimeDetailMapData(new byte[4], new byte[4], 2, 2));

        TestHarness.Assert(loaded, "runtime terrain data should load");
        TestHarness.AssertEqual(0, reader.ReadAllHeightDataCount, "runtime load should not read the full heightmap into CPU memory");
        string componentSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Terrain", "Core", "TerrainComponent.cs"));
        TestHarness.Assert(!componentSource.Contains("RuntimeHeightData", StringComparison.Ordinal), "TerrainComponent should not retain full runtime height data");
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
        => CreateResourceBundle(biomeMaskPath: null);

    private static TerrainRuntimeResourceBundle CreateResourceBundle(string? biomeMaskPath)
    {
        return new TerrainRuntimeResourceBundle
        {
            TerrainDataPath = "fake.terrain",
            BiomeMaskPath = biomeMaskPath ?? CreateExistingBiomeMaskPath(),
            HeightScale = 123.0f,
        };
    }

    private static string CreateExistingBiomeMaskPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "terrain-runtime-load-tests", "biome-mask");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "biome_mask.png");
        if (!File.Exists(path))
        {
            using var image = new Image<Rgba32>(2, 2);
            image.SaveAsPng(path);
        }

        return path;
    }

}
