using Terrain.Editor.Services.Resources;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorMapDataScaffoldTests
{
    public static void RunAll()
    {
        TestHarness.Run("scaffold creates missing authoring tomls with reader-compatible defaults", ScaffoldCreatesMissingAuthoringTomlsWithReaderCompatibleDefaults);
        TestHarness.Run("scaffold leaves existing invalid map definition untouched", ScaffoldLeavesExistingInvalidMapDefinitionUntouched);
    }

    private static void ScaffoldCreatesMissingAuthoringTomlsWithReaderCompatibleDefaults()
    {
        (string root, string appRoot, string gameRoot) = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(gameRoot, "map_data", "materials"));

        GameResourceResolver resolver = GameResourceResolverBootstrap.CreateForAppDirectory(appRoot);

        new EditorMapDataScaffoldService().EnsureScaffold(resolver);

        string defaultToml = Path.Combine(gameRoot, "map_data", "default.toml");
        string descriptorToml = Path.Combine(gameRoot, "map_data", "materials", "descriptor.toml");
        string biomeSettingsToml = Path.Combine(gameRoot, "map_data", "biome_settings.toml");

        TestHarness.Assert(File.Exists(defaultToml), "default.toml should be generated");
        TestHarness.Assert(File.Exists(descriptorToml), "descriptor.toml should be generated");
        TestHarness.Assert(File.Exists(biomeSettingsToml), "biome_settings.toml should be generated");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(defaultToml);
        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(descriptorToml);
        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(biomeSettingsToml);

        TestHarness.AssertEqual("heightmap.png", map.HeightmapPath, "generated default heightmap path");
        TestHarness.AssertEqual("terrain.terrain", map.TerrainDataPath, "generated default terrain path");
        TestHarness.AssertEqual(100.0f, map.HeightScale, "generated default height scale");
        TestHarness.AssertEqual(0, descriptor.Materials.Count, "generated descriptor should start empty");
        TestHarness.AssertEqual(0, settings.Biomes.Count, "generated biome settings should start empty");
        TestHarness.AssertEqual(0, settings.Layers.Count, "generated biome layers should start empty");
    }

    private static void ScaffoldLeavesExistingInvalidMapDefinitionUntouched()
    {
        (string root, string appRoot, string gameRoot) = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(gameRoot, "map_data", "materials"));
        string defaultToml = Path.Combine(gameRoot, "map_data", "default.toml");
        string original = """
version = 1

[terrain]
heightmap = "../bad.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""";
        File.WriteAllText(defaultToml, original);

        GameResourceResolver resolver = GameResourceResolverBootstrap.CreateForAppDirectory(appRoot);

        new EditorMapDataScaffoldService().EnsureScaffold(resolver);

        TestHarness.AssertEqual(original, File.ReadAllText(defaultToml), "existing invalid default.toml should stay untouched");
    }

    private static (string Root, string AppRoot, string GameRoot) CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-editor-map-data-scaffold-tests", Guid.NewGuid().ToString("N"));
        string gameRoot = Path.Combine(root, "game");
        string appRoot = Path.Combine(root, "bin", "Debug", "net8.0");
        Directory.CreateDirectory(gameRoot);
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(root, "Terrain.sln"), string.Empty);
        return (root, appRoot, gameRoot);
    }
}
