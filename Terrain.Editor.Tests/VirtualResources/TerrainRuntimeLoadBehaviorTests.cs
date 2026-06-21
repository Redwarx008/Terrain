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
        TestHarness.Run("terrain runtime load disposes reader when biome mask load fails", TerrainRuntimeLoadDisposesReaderWhenBiomeMaskLoadFails);
        TestHarness.Run("runtime load failure marks component when terrain data is missing", RuntimeLoadFailureMarksComponentWhenTerrainDataIsMissing);
        TestHarness.Run("runtime load failure marks component when biome mask is missing", RuntimeLoadFailureMarksComponentWhenBiomeMaskIsMissing);
        TestHarness.Run("runtime height sampling stays inside terrain streaming", RuntimeHeightSamplingStaysInsideTerrainStreaming);
        TestHarness.Run("runtime height CPU cache follows terrain streaming residency", RuntimeHeightCpuCacheFollowsTerrainStreamingResidency);
        TestHarness.Run("runtime detail map builds after terrain streaming is attached", RuntimeDetailMapBuildsAfterTerrainStreamingIsAttached);
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

    private static void TerrainRuntimeLoadDisposesReaderWhenBiomeMaskLoadFails()
    {
        var component = new TerrainComponent();
        var reader = new FakeTerrainFileReader();
        string missingBiomeMaskPath = Path.Combine(Path.GetTempPath(), "terrain-runtime-load-tests", Guid.NewGuid().ToString("N"), "biome_mask.png");

        TestHarness.AssertThrows<FileNotFoundException>(
            () => TerrainProcessor.CreateLoadedTerrainData(
                component,
                CreateResourceBundle(missingBiomeMaskPath),
                _ => reader,
                static (_, _, _, _) => new RuntimeDetailMapData(new byte[4], new byte[4], 2, 2)),
            "biome mask load failure should surface as FileNotFoundException");

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

    private static void RuntimeDetailMapBuildsAfterTerrainStreamingIsAttached()
    {
        string terrainProcessorSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Terrain", "Core", "TerrainProcessor.cs"));
        int quadTreeIndex = terrainProcessorSource.IndexOf("component.QuadTree = new TerrainQuadTree", StringComparison.Ordinal);
        int detailMapIndex = terrainProcessorSource.IndexOf("RuntimeDetailMapData generatedDetailMaps = loadedData.DetailMapBuilder", StringComparison.Ordinal);
        int injectIndex = terrainProcessorSource.IndexOf("streamingManager.SetGeneratedDetailMaps(generatedDetailMaps)", StringComparison.Ordinal);
        int preloadIndex = terrainProcessorSource.IndexOf("streamingManager.PreloadTopLevelChunks", StringComparison.Ordinal);

        TestHarness.Assert(quadTreeIndex >= 0, "terrain quad tree should be attached before runtime detail map generation");
        TestHarness.Assert(detailMapIndex > quadTreeIndex, "runtime detail map generation should happen after TerrainComponent.GetHeight is backed by streaming");
        TestHarness.Assert(injectIndex > detailMapIndex, "generated runtime detail maps should be provided to the streaming upload path after generation");
        TestHarness.Assert(preloadIndex > injectIndex, "top-level detail pages should preload only after generated detail maps are available");
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

    private static void RuntimeDetailMapUsesTerrainComponentHeightInterface()
    {
        string terrainProcessorSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Terrain", "Core", "TerrainProcessor.cs"));

        TestHarness.Assert(terrainProcessorSource.Contains("RuntimeDetailMapBuilder.Generate(\r\n            component.GetHeight,", StringComparison.Ordinal)
            || terrainProcessorSource.Contains("RuntimeDetailMapBuilder.Generate(\n            component.GetHeight,", StringComparison.Ordinal),
            "runtime detail map generation should sample through TerrainComponent.GetHeight");
        TestHarness.Assert(!terrainProcessorSource.Contains("ReadAllHeightData()", StringComparison.Ordinal), "runtime detail map generation should not read the full heightmap");
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
