#nullable enable

namespace Terrain.Editor.Tests;

internal static class RuntimeOceanAssetTests
{
    public static void RunAll()
    {
        TestHarness.Run("main scene contains map surface and ocean entities", MainSceneContainsMapSurfaceAndOceanEntities);
        TestHarness.Run("graphics compositor contains ocean render feature", GraphicsCompositorContainsOceanRenderFeature);
        TestHarness.Run("editor scaffold default map writes sea level", EditorScaffoldDefaultMapWritesSeaLevel);
    }

    private static void MainSceneContainsMapSurfaceAndOceanEntities()
    {
        string text = File.ReadAllText(Path.Combine("Terrain", "Assets", "MainScene.sdscene"));

        TestHarness.Assert(text.Contains("Name: MapSurface", StringComparison.Ordinal), "MainScene should contain the map surface entity");
        TestHarness.Assert(text.Contains("!Terrain.MapSurface.MapSurfaceComponent,Terrain", StringComparison.Ordinal), "MainScene should contain MapSurfaceComponent");
        TestHarness.Assert(text.Contains("Name: Ocean", StringComparison.Ordinal), "MainScene should contain Ocean entity");
        TestHarness.Assert(text.Contains("!Terrain.Rendering.Ocean.OceanComponent,Terrain", StringComparison.Ordinal), "MainScene should contain OceanComponent");
        TestHarness.Assert(!text.Contains("OceanEntity: null", StringComparison.Ordinal), "MainScene MapSurfaceComponent should reference Ocean entity");
    }

    private static void GraphicsCompositorContainsOceanRenderFeature()
    {
        string text = File.ReadAllText(Path.Combine("Terrain", "Assets", "GraphicsCompositor.sdgfxcomp"));

        TestHarness.Assert(text.Contains("!Terrain.Rendering.Ocean.OceanRenderFeature,Terrain", StringComparison.Ordinal), "GraphicsCompositor should register OceanRenderFeature");
        TestHarness.Assert(text.Contains("EffectName: OceanSurface", StringComparison.Ordinal), "GraphicsCompositor should route OceanSurface");
        int oceanFeature = text.IndexOf("!Terrain.Rendering.Ocean.OceanRenderFeature,Terrain", StringComparison.Ordinal);
        int oceanEffect = text.IndexOf("EffectName: OceanSurface", oceanFeature, StringComparison.Ordinal);
        int waterStage = text.LastIndexOf("RenderStage: ref!! 6b596b72-f95b-4f48-9f75-bf73b61e9fe9", oceanEffect, StringComparison.Ordinal);
        int transparentStage = text.LastIndexOf("RenderStage: ref!! 0fbd7f2d-8037-4033-9616-14d59c88b1fd", oceanEffect, StringComparison.Ordinal);
        TestHarness.Assert(waterStage > oceanFeature, "GraphicsCompositor should route OceanSurface to the Water stage");
        TestHarness.Assert(transparentStage < oceanFeature, "GraphicsCompositor should not route OceanSurface to the generic Transparent stage");
    }

    private static void EditorScaffoldDefaultMapWritesSeaLevel()
    {
        string text = File.ReadAllText(Path.Combine("Terrain.Editor", "Services", "Resources", "EditorMapDataScaffoldService.cs"));

        TestHarness.Assert(text.Contains("SeaLevel = 3.8f", StringComparison.Ordinal), "scaffolded default map should persist sea level");
    }
}
