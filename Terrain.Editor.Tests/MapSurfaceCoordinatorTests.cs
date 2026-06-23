#nullable enable

using System.Reflection;
using Stride.Engine.Design;
using Terrain.MapSurface;
using Terrain.Resources;

namespace Terrain.Editor.Tests;

internal static class MapSurfaceCoordinatorTests
{
    public static void RunAll()
    {
        TestHarness.Run("map surface component uses processor", MapSurfaceComponentUsesProcessor);
        TestHarness.Run("map surface component does not expose sea level property", MapSurfaceComponentDoesNotExposeSeaLevelProperty);
        TestHarness.Run("terrain component accepts coordinator runtime bundle", TerrainComponentAcceptsCoordinatorRuntimeBundle);
        TestHarness.Run("runtime scene contains map surface coordinator", RuntimeSceneContainsMapSurfaceCoordinator);
        TestHarness.Run("map surface resource load failure is latched", MapSurfaceResourceLoadFailureIsLatched);
    }

    private static void MapSurfaceComponentUsesProcessor()
    {
        var attribute = typeof(MapSurfaceComponent)
            .GetCustomAttributes()
            .OfType<DefaultEntityComponentProcessorAttribute>()
            .FirstOrDefault();

        TestHarness.Assert(attribute != null, "MapSurfaceComponent should register a non-render entity processor");
        TestHarness.AssertEqual(typeof(MapSurfaceProcessor).AssemblyQualifiedName, attribute!.TypeName, "processor type");
    }

    private static void MapSurfaceComponentDoesNotExposeSeaLevelProperty()
    {
        PropertyInfo? seaLevel = typeof(MapSurfaceComponent).GetProperty(
            "SeaLevel",
            BindingFlags.Instance | BindingFlags.Public);

        TestHarness.Assert(seaLevel == null, "SeaLevel should stay in map settings, not MapSurfaceComponent");
    }

    private static void TerrainComponentAcceptsCoordinatorRuntimeBundle()
    {
        var terrain = new TerrainComponent();
        var bundle = new TerrainRuntimeResourceBundle
        {
            TerrainDataPath = "map/terrain.terrain",
            HeightScale = 200.0f,
            SeaLevel = 9.25f,
        };

        terrain.ApplyRuntimeResourceBundle(bundle);

        TestHarness.Assert(ReferenceEquals(bundle, terrain.RuntimeResourceBundle), "Terrain should keep coordinator resource bundle");
    }

    private static void RuntimeSceneContainsMapSurfaceCoordinator()
    {
        string scene = ReadRepositoryText("Terrain/Assets/MainScene.sdscene");

        TestHarness.Assert(scene.Contains("Name: MapSurface", StringComparison.Ordinal), "MainScene should define a MapSurface coordinator entity");
        TestHarness.Assert(scene.Contains("!Terrain.MapSurface.MapSurfaceComponent,Terrain", StringComparison.Ordinal), "MainScene should attach MapSurfaceComponent");
        TestHarness.Assert(scene.Contains("TerrainEntity: ref!! 1cfe1131-cc2f-4a41-947d-3102b1f351dd", StringComparison.Ordinal), "MapSurface should reference the existing Terrain entity");
        TestHarness.Assert(scene.Contains("RiverEntity: ref!! c8f8f226-3477-45ec-84d8-d8e8de365e1b", StringComparison.Ordinal), "MapSurface should reference the existing RiverSystem entity");
        TestHarness.Assert(scene.Contains("OceanEntity: ref!!", StringComparison.Ordinal), "MapSurface should reference an Ocean entity once ocean wiring is enabled");
    }

    private static void MapSurfaceResourceLoadFailureIsLatched()
    {
        var state = new MapSurfaceRuntimeState();
        int attempts = 0;
        var diagnostics = new List<string>();

        bool firstResult = MapSurfaceProcessor.TryEnsureResources(
            state,
            () =>
            {
                attempts++;
                throw new InvalidDataException("missing default.toml");
            },
            diagnostics.Add,
            out TerrainRuntimeResourceBundle? firstResources);

        bool secondResult = MapSurfaceProcessor.TryEnsureResources(
            state,
            () =>
            {
                attempts++;
                throw new InvalidDataException("should not retry");
            },
            diagnostics.Add,
            out TerrainRuntimeResourceBundle? secondResources);

        TestHarness.Assert(!firstResult, "First failed resource load should return false");
        TestHarness.Assert(!secondResult, "Latched failed resource load should return false");
        TestHarness.Assert(firstResources == null, "First failed resource load should not produce resources");
        TestHarness.Assert(secondResources == null, "Latched failed resource load should not produce resources");
        TestHarness.AssertEqual(1, attempts, "MapSurface should not retry a latched resource load failure every frame");
        TestHarness.AssertEqual(1, diagnostics.Count, "MapSurface should log the resource load failure once");
        TestHarness.Assert(state.ResourceLoadFailed, "MapSurface should remember resource load failure");
    }

    private static string ReadRepositoryText(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            string gitPath = Path.Combine(directory, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
