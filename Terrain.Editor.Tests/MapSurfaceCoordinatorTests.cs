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
}
