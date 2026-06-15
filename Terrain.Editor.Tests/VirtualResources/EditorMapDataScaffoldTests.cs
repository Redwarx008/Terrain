using Terrain.Editor.Services.Resources;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorMapDataScaffoldTests
{
    public static void RunAll()
    {
        TestHarness.Run("scaffold creates missing authoring tomls with reader-compatible defaults", ScaffoldCreatesMissingAuthoringTomlsWithReaderCompatibleDefaults);
        TestHarness.Run("scaffold writes top comment templates without breaking reader defaults", ScaffoldWritesTopCommentTemplatesWithoutBreakingReaderDefaults);
        TestHarness.Run("scaffold leaves existing invalid map definition untouched", ScaffoldLeavesExistingInvalidMapDefinitionUntouched);
    }

    private static void ScaffoldCreatesMissingAuthoringTomlsWithReaderCompatibleDefaults()
    {
        (string root, string appRoot, string gameRoot) = CreateWorkspace();

        GameResourceResolver resolver = GameResourceResolverBootstrap.CreateForAppDirectory(appRoot);

        string defaultToml = Path.Combine(gameRoot, "map_data", "default.toml");
        string descriptorToml = Path.Combine(gameRoot, "map_data", "materials", "descriptor.toml");
        string biomeSettingsToml = Path.Combine(gameRoot, "map_data", "biome_settings.toml");
        string materialsDirectory = Path.GetDirectoryName(descriptorToml)!;

        TestHarness.Assert(!Directory.Exists(materialsDirectory), "materials directory should start missing for cold-start scaffold coverage");

        new EditorMapDataScaffoldService().EnsureScaffold(resolver);

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
        TestHarness.AssertEqual(0, settings.Modifiers.Count, "generated biome modifiers should start empty");
    }

    private static void ScaffoldWritesTopCommentTemplatesWithoutBreakingReaderDefaults()
    {
        (string root, string appRoot, string gameRoot) = CreateWorkspace();

        GameResourceResolver resolver = GameResourceResolverBootstrap.CreateForAppDirectory(appRoot);

        string defaultToml = Path.Combine(gameRoot, "map_data", "default.toml");
        string descriptorToml = Path.Combine(gameRoot, "map_data", "materials", "descriptor.toml");
        string biomeSettingsToml = Path.Combine(gameRoot, "map_data", "biome_settings.toml");

        new EditorMapDataScaffoldService().EnsureScaffold(resolver);

        string defaultText = File.ReadAllText(defaultToml);
        string descriptorText = File.ReadAllText(descriptorToml);
        string biomeSettingsText = File.ReadAllText(biomeSettingsToml);

        TestHarness.Assert(defaultText.StartsWith("# Optional terrain companion resources:", StringComparison.Ordinal), "default.toml should begin with the optional terrain comment template");
        TestHarness.Assert(defaultText.Contains("# rivers = \"rivers.png\"", StringComparison.Ordinal), "default.toml should include rivers example comment");
        TestHarness.Assert(defaultText.Contains("# provinces = \"provinces.png\"", StringComparison.Ordinal), "default.toml should include provinces example comment");

        TestHarness.Assert(descriptorText.StartsWith("# Example material:", StringComparison.Ordinal), "descriptor.toml should begin with the example material template");
        TestHarness.Assert(descriptorText.Contains("# [[materials]]", StringComparison.Ordinal), "descriptor.toml should include materials example comment");
        TestHarness.Assert(descriptorText.Contains("# id = \"plains\"", StringComparison.Ordinal), "descriptor.toml should include material id example comment");
        TestHarness.Assert(descriptorText.Contains("# index = 0", StringComparison.Ordinal), "descriptor.toml should include material index example comment");
        TestHarness.Assert(descriptorText.Contains("# name = \"Plains\"", StringComparison.Ordinal), "descriptor.toml should include material name example comment");
        TestHarness.Assert(descriptorText.Contains("# albedo = \"plains_01_diffuse.dds\"", StringComparison.Ordinal), "descriptor.toml should include material albedo example comment");

        TestHarness.Assert(biomeSettingsText.StartsWith("# Example biome:", StringComparison.Ordinal), "biome_settings.toml should begin with the example biome template");
        TestHarness.Assert(biomeSettingsText.Contains("# [[biomes]]", StringComparison.Ordinal), "biome_settings.toml should include biomes example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# [[layers]]", StringComparison.Ordinal), "biome_settings.toml should include layers example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# [[modifiers]]", StringComparison.Ordinal), "biome_settings.toml should include modifiers example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# id = 1", StringComparison.Ordinal), "biome_settings.toml should include biome id example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# name = \"Default\"", StringComparison.Ordinal), "biome_settings.toml should include biome name example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# biome_id = 1", StringComparison.Ordinal), "biome_settings.toml should include layer biome id example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# material_id = \"plains\"", StringComparison.Ordinal), "biome_settings.toml should include layer material id example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# enabled = true", StringComparison.Ordinal), "biome_settings.toml should include enabled example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# type = \"slope\"", StringComparison.Ordinal), "biome_settings.toml should include modifier type example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# blend_mode = \"add\"", StringComparison.Ordinal), "biome_settings.toml should include modifier blend mode example comment");
        TestHarness.Assert(biomeSettingsText.Contains("# opacity = 1.0", StringComparison.Ordinal), "biome_settings.toml should include modifier opacity example comment");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(defaultToml);
        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(descriptorToml);
        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(biomeSettingsToml);

        TestHarness.AssertEqual("heightmap.png", map.HeightmapPath, "generated default heightmap path");
        TestHarness.AssertEqual("terrain.terrain", map.TerrainDataPath, "generated default terrain path");
        TestHarness.AssertEqual(100.0f, map.HeightScale, "generated default height scale");
        TestHarness.AssertEqual(0, descriptor.Materials.Count, "generated descriptor should start empty");
        TestHarness.AssertEqual(0, settings.Biomes.Count, "generated biome settings should start empty");
        TestHarness.AssertEqual(0, settings.Layers.Count, "generated biome layers should start empty");
        TestHarness.AssertEqual(0, settings.Modifiers.Count, "generated biome modifiers should start empty");
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
        Directory.CreateDirectory(Path.Combine(gameRoot, "map_data"));
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(root, "Terrain.sln"), string.Empty);
        return (root, appRoot, gameRoot);
    }
}
