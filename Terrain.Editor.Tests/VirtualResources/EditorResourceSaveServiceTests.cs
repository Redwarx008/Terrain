using Terrain.Editor.Services;
using Terrain.Editor.Services.Resources;
using Terrain.Editor.Tests;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorResourceSaveServiceTests
{
    public static void RunAll()
    {
        TestHarness.Run("authoring save rolls back earlier files when a later writer fails", AuthoringSaveRollsBackEarlierFilesWhenLaterWriterFails);
    }

    private static void AuthoringSaveRollsBackEarlierFilesWhenLaterWriterFails()
    {
        string root = CreateWorkspace();
        string mapDefinitionPath = Path.Combine(root, "mod", "map_data", "default.toml");
        string heightmapPath = Path.Combine(root, "mod", "map_data", "heightmap.png");
        string biomeMaskPath = Path.Combine(root, "mod", "map_data", "biome_mask.png");
        string biomeSettingsPath = Path.Combine(root, "mod", "map_data", "biome_settings.toml");
        string materialDescriptorPath = Path.Combine(root, "mod", "map_data", "materials", "descriptor.toml");

        Directory.CreateDirectory(Path.GetDirectoryName(materialDescriptorPath)!);
        File.WriteAllText(mapDefinitionPath, "original-default");
        File.WriteAllText(heightmapPath, "original-heightmap");
        File.WriteAllText(biomeMaskPath, "original-biome-mask");
        File.WriteAllText(biomeSettingsPath, "original-biome-settings");
        File.WriteAllText(materialDescriptorPath, "original-material-descriptor");

        EditorResourceSession session = CreateSession(
            root,
            mapDefinitionPath,
            heightmapPath,
            biomeMaskPath,
            biomeSettingsPath,
            materialDescriptorPath);

        var biomeMask = new BiomeMask(2, 2);
        biomeMask.SetValue(0, 0, 1);
        biomeMask.SetValue(1, 1, 2);

        TestHarness.AssertThrows<InvalidDataException>(
            () => EditorResourceSaveService.Save(
                session,
                [1, 2, 3, 4],
                width: 2,
                height: 2,
                biomeMask,
                heightScale: 222.0f,
                descriptorSlots:
                [
                    new EditorMaterialDescriptorSlot("grass", 0, "Grass", "nested/grass.png", null, null),
                ],
                biomeSnapshot: new EditorBiomeSettingsSnapshot([], [], [])),
            "invalid material descriptor path should fail the save");

        TestHarness.AssertEqual("original-default", File.ReadAllText(mapDefinitionPath), "map definition should roll back");
        TestHarness.AssertEqual("original-heightmap", File.ReadAllText(heightmapPath), "heightmap should roll back");
        TestHarness.AssertEqual("original-biome-mask", File.ReadAllText(biomeMaskPath), "biome mask should roll back");
        TestHarness.AssertEqual("original-biome-settings", File.ReadAllText(biomeSettingsPath), "biome settings should roll back");
        TestHarness.AssertEqual("original-material-descriptor", File.ReadAllText(materialDescriptorPath), "material descriptor should roll back");
    }

    private static EditorResourceSession CreateSession(
        string root,
        string mapDefinitionPath,
        string heightmapPath,
        string biomeMaskPath,
        string biomeSettingsPath,
        string materialDescriptorPath)
    {
        static ResolvedGameResource Resource(string virtualPath, string path)
        {
            return new ResolvedGameResource(virtualPath, path, "mod", IsWritable: true, HasLowerPriorityFallback: true);
        }

        return new EditorResourceSession(
            Resource("map_data/default.toml", mapDefinitionPath),
            Resource("map_data/heightmap.png", heightmapPath),
            Resource("map_data/terrain.terrain", Path.Combine(root, "mod", "map_data", "terrain.terrain")),
            Resource("map_data/biome_mask.png", biomeMaskPath),
            Resource("map_data/biome_settings.toml", biomeSettingsPath),
            Resource("map_data/materials/descriptor.toml", materialDescriptorPath),
            new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 100.0f,
            });
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-editor-save-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
