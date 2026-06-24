#nullable enable

namespace Terrain.Editor.Tests;

internal static class RuntimeOceanAssetTests
{
    public static void RunAll()
    {
        TestHarness.Run("main scene contains map surface and ocean entities", MainSceneContainsMapSurfaceAndOceanEntities);
        TestHarness.Run("main scene ocean material uses CK3-hue lighting-calibrated water colors", MainSceneOceanMaterialUsesCk3HueLightingCalibratedWaterColors);
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

    private static void MainSceneOceanMaterialUsesCk3HueLightingCalibratedWaterColors()
    {
        string scene = File.ReadAllText(Path.Combine("Terrain", "Assets", "MainScene.sdscene"));
        string settings = File.ReadAllText(Path.Combine("Terrain", "Rendering", "Ocean", "OceanMaterialSettings.cs"));

        TestHarness.Assert(scene.Contains("ShallowColor: {R: 0.035, G: 0.09, B: 0.11}", StringComparison.Ordinal), "MainScene Ocean material should serialize the CK3-hue shallow water color");
        TestHarness.Assert(scene.Contains("DeepColor: {R: 0.006, G: 0.024, B: 0.034}", StringComparison.Ordinal), "MainScene Ocean material should serialize the CK3-hue deep water color");
        TestHarness.Assert(settings.Contains("new(0.035f, 0.09f, 0.11f)", StringComparison.Ordinal), "OceanMaterialSettings should default to the CK3-hue shallow water color");
        TestHarness.Assert(settings.Contains("new(0.006f, 0.024f, 0.034f)", StringComparison.Ordinal), "OceanMaterialSettings should default to the CK3-hue deep water color");
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
