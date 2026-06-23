using Terrain.Editor.Services.Resources;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorMapDataScaffoldTests
{
    private static readonly string[] MapDefinitionTemplateLines =
    [
        "# Optional terrain companion resources:",
        "# rivers = \"rivers.png\"",
        "# provinces = \"provinces.png\"",
    ];

    private static readonly string[] MaterialDescriptorTemplateLines =
    [
        "# Example material:",
        "# [[materials]]",
        "# id = \"plains\"",
        "# index = 0",
        "# name = \"Plains\"",
        "# albedo = \"plains_01_diffuse.dds\"",
        "# normal = \"plains_01_normal.dds\"",
        "# properties = \"plains_01_properties.dds\"",
    ];

    private static readonly string[] BiomeSettingsTemplateLines =
    [
        "# Example biome:",
        "# [[biomes]]",
        "# id = 1",
        "# name = \"Default\"",
        "#",
        "# Example layer:",
        "# [[layers]]",
        "# id = 1",
        "# biome_id = 1",
        "# name = \"Base\"",
        "# material_id = \"plains\"",
        "# priority = 0",
        "# enabled = true",
        "# visible = true",
        "#",
        "# Example modifier:",
        "# [[modifiers]]",
        "# id = 1",
        "# layer_id = 1",
        "# name = \"Slope\"",
        "# type = \"slope\"",
        "# blend_mode = \"add\"",
        "# min = 0.2",
        "# max = 0.8",
        "# min_falloff = 0.1",
        "# max_falloff = 0.1",
        "# opacity = 1.0",
        "# enabled = true",
        "# visible = true",
    ];

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

        string defaultToml = Path.Combine(gameRoot, "map", "default.toml");
        string descriptorToml = Path.Combine(gameRoot, "map", "materials", "descriptor.toml");
        string biomeSettingsToml = Path.Combine(gameRoot, "map", "biome_settings.toml");
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
        TestHarness.AssertEqual(3000.0f, map.RiverMaxVisibleCameraHeight, "generated default river max visible camera height");
        TestHarness.AssertEqual(3.8f, map.SeaLevel, "generated default sea level");
        TestHarness.AssertEqual(0, descriptor.Materials.Count, "generated descriptor should start empty");
        TestHarness.AssertEqual(0, settings.Biomes.Count, "generated biome settings should start empty");
        TestHarness.AssertEqual(0, settings.Layers.Count, "generated biome layers should start empty");
        TestHarness.AssertEqual(0, settings.Modifiers.Count, "generated biome modifiers should start empty");
    }

    private static void ScaffoldWritesTopCommentTemplatesWithoutBreakingReaderDefaults()
    {
        (string root, string appRoot, string gameRoot) = CreateWorkspace();

        GameResourceResolver resolver = GameResourceResolverBootstrap.CreateForAppDirectory(appRoot);

        string defaultToml = Path.Combine(gameRoot, "map", "default.toml");
        string descriptorToml = Path.Combine(gameRoot, "map", "materials", "descriptor.toml");
        string biomeSettingsToml = Path.Combine(gameRoot, "map", "biome_settings.toml");

        new EditorMapDataScaffoldService().EnsureScaffold(resolver);

        string defaultText = File.ReadAllText(defaultToml);
        string descriptorText = File.ReadAllText(descriptorToml);
        string biomeSettingsText = File.ReadAllText(biomeSettingsToml);

        AssertStartsWithTemplateBlock(defaultText, MapDefinitionTemplateLines, "default.toml");
        AssertStartsWithTemplateBlock(descriptorText, MaterialDescriptorTemplateLines, "descriptor.toml");
        AssertStartsWithTemplateBlock(biomeSettingsText, BiomeSettingsTemplateLines, "biome_settings.toml");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(defaultToml);
        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(descriptorToml);
        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(biomeSettingsToml);

        TestHarness.AssertEqual("heightmap.png", map.HeightmapPath, "generated default heightmap path");
        TestHarness.AssertEqual("terrain.terrain", map.TerrainDataPath, "generated default terrain path");
        TestHarness.AssertEqual(100.0f, map.HeightScale, "generated default height scale");
        TestHarness.Assert(defaultText.Contains("river_max_visible_camera_height = 3000", StringComparison.Ordinal), "generated default.toml should write river max visible camera height");
        TestHarness.AssertEqual(3000.0f, map.RiverMaxVisibleCameraHeight, "generated default river max visible camera height");
        TestHarness.Assert(defaultText.Contains("sea_level = 3.8", StringComparison.Ordinal), "generated default.toml should write sea level");
        TestHarness.AssertEqual(3.8f, map.SeaLevel, "generated default sea level");
        TestHarness.AssertEqual(0, descriptor.Materials.Count, "generated descriptor should start empty");
        TestHarness.AssertEqual(0, settings.Biomes.Count, "generated biome settings should start empty");
        TestHarness.AssertEqual(0, settings.Layers.Count, "generated biome layers should start empty");
        TestHarness.AssertEqual(0, settings.Modifiers.Count, "generated biome modifiers should start empty");
    }

    private static void ScaffoldLeavesExistingInvalidMapDefinitionUntouched()
    {
        (string root, string appRoot, string gameRoot) = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(gameRoot, "map", "materials"));
        string defaultToml = Path.Combine(gameRoot, "map", "default.toml");
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
        Directory.CreateDirectory(Path.Combine(gameRoot, "map"));
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(root, "Terrain.sln"), string.Empty);
        return (root, appRoot, gameRoot);
    }

    private static void AssertStartsWithTemplateBlock(string text, IReadOnlyList<string> templateLines, string fileLabel)
    {
        string normalized = NormalizeLineEndings(text);
        string expectedPrefix = string.Join('\n', templateLines) + "\n\n";
        TestHarness.Assert(normalized.StartsWith(expectedPrefix, StringComparison.Ordinal), $"{fileLabel} should begin with the full template block followed by a blank line");
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
