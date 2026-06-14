using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class GameRuntimeResourceBootstrapTests
{
    public static void RunAll()
    {
        TestHarness.Run("bootstrap loads fixed companion resources", BootstrapLoadsFixedCompanionResources);
        TestHarness.Run("bootstrap does not require heightmap resource", BootstrapDoesNotRequireHeightmapResource);
        TestHarness.Run("bootstrap does not require heightmap declaration", BootstrapDoesNotRequireHeightmapDeclaration);
        TestHarness.Run("bootstrap ignores invalid heightmap declaration", BootstrapIgnoresInvalidHeightmapDeclaration);
        TestHarness.Run("bootstrap requires terrain data resource", BootstrapRequiresTerrainDataResource);
        TestHarness.Run("bootstrap requires biome mask resource", BootstrapRequiresBiomeMaskResource);
        TestHarness.Run("bootstrap keeps rivers optional", BootstrapKeepsRiversOptional);
        TestHarness.Run("bootstrap resolves declared rivers when present", BootstrapResolvesDeclaredRiversWhenPresent);
        TestHarness.Run("bootstrap keeps declared rivers missing optional", BootstrapKeepsDeclaredRiversMissingOptional);
        TestHarness.Run("bootstrap reports provinces as declared but not implemented", BootstrapReportsProvincesAsDeclaredButNotImplemented);
        TestHarness.Run("bootstrap validates biome material references", BootstrapValidatesBiomeMaterialReferences);
        TestHarness.Run("bootstrap validates biome material references case sensitively", BootstrapValidatesBiomeMaterialReferencesCaseSensitively);
        TestHarness.Run("bootstrap follows resolver override order", BootstrapFollowsResolverOverrideOrder);
        TestHarness.Run("bootstrap resolves material textures through resource layers", BootstrapResolvesMaterialTexturesThroughResourceLayers);
    }

    private static void BootstrapLoadsFixedCompanionResources()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, heightScale: 250.0f, includeRivers: true);

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

        TestHarness.AssertEqual(FullPath(root, "map_data", "terrain.terrain"), bundle.TerrainDataPath, "terrain data path");
        TestHarness.AssertEqual(FullPath(root, "map_data", "biome_mask.png"), bundle.BiomeMaskPath, "biome mask path");
        TestHarness.AssertEqual(FullPath(root, "map_data", "biome_settings.toml"), bundle.BiomeSettingsPath, "biome settings path");
        TestHarness.AssertEqual(FullPath(root, "map_data", "materials", "descriptor.toml"), bundle.MaterialDescriptorPath, "material descriptor path");
        TestHarness.AssertEqual(FullPath(root, "map_data", "materials"), bundle.MaterialsDirectory, "materials directory");
        TestHarness.AssertEqual(FullPath(root, "map_data", "materials", "grass_a.png"), bundle.MaterialTextureSlots[0].AlbedoPath, "resolved albedo path");
        TestHarness.AssertEqual(FullPath(root, "map_data", "rivers.png"), bundle.RiversPath, "rivers path");
        TestHarness.AssertEqual(250.0f, bundle.HeightScale, "height scale");
        TestHarness.AssertEqual(1, bundle.MaterialDescriptor.Materials.Count, "material count");
        TestHarness.AssertEqual(1, bundle.BiomeSettings.Layers.Count, "biome layer count");
    }

    private static void BootstrapDoesNotRequireHeightmapResource()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root);
        File.Delete(Path.Combine(root, "map_data", "heightmap.png"));

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

        TestHarness.AssertEqual(FullPath(root, "map_data", "terrain.terrain"), bundle.TerrainDataPath, "runtime should still resolve terrain data");
    }

    private static void BootstrapIgnoresInvalidHeightmapDeclaration()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, heightmapDeclaration: "heightmap = \"C:/absolute/ignored.png\"");

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

        TestHarness.AssertEqual(FullPath(root, "map_data", "terrain.terrain"), bundle.TerrainDataPath, "runtime should ignore invalid heightmap declaration");
    }

    private static void BootstrapDoesNotRequireHeightmapDeclaration()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, heightmapDeclaration: string.Empty);

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

        TestHarness.AssertEqual(FullPath(root, "map_data", "terrain.terrain"), bundle.TerrainDataPath, "runtime should not require heightmap declaration");
    }

    private static void BootstrapRequiresTerrainDataResource()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root);
        File.Delete(Path.Combine(root, "map_data", "terrain.terrain"));

        FileNotFoundException ex = TestHarness.AssertThrows<FileNotFoundException>(
            () => new GameRuntimeResourceBootstrap(CreateResolver(root)).Load(),
            "missing terrain data should throw FileNotFoundException");

        TestHarness.AssertEqual("map_data/terrain.terrain", ex.FileName, "missing terrain data virtual path");
    }

    private static void BootstrapRequiresBiomeMaskResource()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root);
        File.Delete(Path.Combine(root, "map_data", "biome_mask.png"));

        FileNotFoundException ex = TestHarness.AssertThrows<FileNotFoundException>(
            () => new GameRuntimeResourceBootstrap(CreateResolver(root)).Load(),
            "missing biome mask should throw FileNotFoundException");

        TestHarness.AssertEqual("map_data/biome_mask.png", ex.FileName, "missing biome mask virtual path");
    }

    private static void BootstrapKeepsRiversOptional()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, includeRivers: false);

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

        TestHarness.AssertEqual(null, bundle.RiversPath, "rivers path should stay null when not declared");
    }

    private static void BootstrapResolvesDeclaredRiversWhenPresent()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, includeRivers: true);

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

        TestHarness.AssertEqual(FullPath(root, "map_data", "rivers.png"), bundle.RiversPath, "declared rivers path");
    }

    private static void BootstrapKeepsDeclaredRiversMissingOptional()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, includeRivers: true, createRiversFile: false);

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

        TestHarness.AssertEqual(null, bundle.RiversPath, "missing declared rivers path should stay null");
        TestHarness.Assert(
            bundle.Diagnostics.Any(diagnostic =>
                diagnostic.Contains("rivers", StringComparison.OrdinalIgnoreCase) &&
                (diagnostic.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
                 diagnostic.Contains("not found", StringComparison.OrdinalIgnoreCase))),
            "missing declared rivers diagnostic should mention rivers and missing/not found");
    }

    private static void BootstrapReportsProvincesAsDeclaredButNotImplemented()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, includeProvinces: true);

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(CreateResolver(root)).Load();

        TestHarness.Assert(bundle.HasDeclaredProvinces, "provinces should be reported as declared");
        TestHarness.Assert(
            bundle.Diagnostics.Any(diagnostic =>
                diagnostic.Contains("provinces", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.Contains("not implemented", StringComparison.OrdinalIgnoreCase)),
            "provinces diagnostic should mention not implemented");
    }

    private static void BootstrapValidatesBiomeMaterialReferences()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, biomeMaterialId: "missing");

        AssertThrowsInvalidData(
            () => new GameRuntimeResourceBootstrap(CreateResolver(root)).Load(),
            "unknown biome material_id should throw InvalidDataException");
    }

    private static void BootstrapValidatesBiomeMaterialReferencesCaseSensitively()
    {
        string root = CreateWorkspace();
        WriteResourceBundle(root, biomeMaterialId: "Grassland");

        AssertThrowsInvalidData(
            () => new GameRuntimeResourceBootstrap(CreateResolver(root)).Load(),
            "material_id should be case sensitive");
    }

    private static void BootstrapFollowsResolverOverrideOrder()
    {
        string root = CreateWorkspace();
        string baseRoot = Path.Combine(root, "base");
        string modRoot = Path.Combine(root, "mod");
        WriteResourceBundle(baseRoot, heightScale: 100.0f);
        WriteResourceBundle(modRoot, heightScale: 333.0f, heightmapText: "mod heightmap");

        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", baseRoot, isBaseLayer: true),
            new GameResourceLayer("mod", modRoot, isBaseLayer: false),
        });

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(resolver).Load();

        TestHarness.AssertEqual(333.0f, bundle.HeightScale, "height scale should come from highest priority default.toml");
        TestHarness.AssertEqual(FullPath(modRoot, "map_data", "terrain.terrain"), bundle.TerrainDataPath, "terrain data should come from highest priority layer");
    }

    private static void BootstrapResolvesMaterialTexturesThroughResourceLayers()
    {
        string root = CreateWorkspace();
        string baseRoot = Path.Combine(root, "base");
        string modRoot = Path.Combine(root, "mod");
        WriteResourceBundle(baseRoot);
        WriteResourceBundle(modRoot);
        File.Delete(Path.Combine(modRoot, "map_data", "materials", "grass_a.png"));
        File.WriteAllText(Path.Combine(baseRoot, "map_data", "materials", "grass_n.png"), "base normal");
        File.WriteAllText(Path.Combine(modRoot, "map_data", "materials", "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
albedo = "grass_a.png"
normal = "grass_n.png"
""");

        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", baseRoot, isBaseLayer: true),
            new GameResourceLayer("mod", modRoot, isBaseLayer: false),
        });

        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(resolver).Load();

        TestHarness.AssertEqual(FullPath(baseRoot, "map_data", "materials", "grass_a.png"), bundle.MaterialTextureSlots[0].AlbedoPath, "missing mod albedo should fall back to base");
        TestHarness.AssertEqual(FullPath(baseRoot, "map_data", "materials", "grass_n.png"), bundle.MaterialTextureSlots[0].NormalPath, "missing mod normal should fall back to base");
    }

    private static GameResourceResolver CreateResolver(string root)
    {
        return new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        });
    }

    private static void WriteResourceBundle(
        string root,
        float heightScale = 120.0f,
        bool includeRivers = false,
        bool createRiversFile = true,
        bool includeProvinces = false,
        string biomeMaterialId = "grassland",
        string heightmapText = "heightmap",
        string heightmapDeclaration = "heightmap = \"heightmap.png\"")
    {
        string mapData = Path.Combine(root, "map_data");
        string materials = Path.Combine(mapData, "materials");
        Directory.CreateDirectory(materials);

        File.WriteAllText(Path.Combine(mapData, "heightmap.png"), heightmapText);
        File.WriteAllText(Path.Combine(mapData, "terrain.terrain"), "terrain");
        File.WriteAllText(Path.Combine(mapData, "biome_mask.png"), "mask");
        File.WriteAllText(Path.Combine(materials, "grass_a.png"), "albedo");

        if (includeRivers && createRiversFile)
            File.WriteAllText(Path.Combine(mapData, "rivers.png"), "rivers");

        File.WriteAllText(Path.Combine(mapData, "default.toml"), CreateDefaultToml(heightScale, includeRivers, includeProvinces, heightmapDeclaration));
        File.WriteAllText(Path.Combine(materials, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
albedo = "grass_a.png"
""");
        File.WriteAllText(Path.Combine(mapData, "biome_settings.toml"), $$"""
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "{{biomeMaterialId}}"
priority = 0
enabled = true
visible = true
""");
    }

    private static string CreateDefaultToml(float heightScale, bool includeRivers, bool includeProvinces)
    {
        return CreateDefaultToml(
            heightScale,
            includeRivers,
            includeProvinces,
            "heightmap = \"heightmap.png\"");
    }

    private static string CreateDefaultToml(float heightScale, bool includeRivers, bool includeProvinces, string heightmapDeclaration)
    {
        string heightmapLine = string.IsNullOrWhiteSpace(heightmapDeclaration)
            ? string.Empty
            : $"{heightmapDeclaration}\n";

        return $$"""
version = 1

[terrain]
{{heightmapLine}}
terrain_data = "terrain.terrain"
{{(includeRivers ? "rivers = \"rivers.png\"" : string.Empty)}}
{{(includeProvinces ? "provinces = \"provinces.png\"" : string.Empty)}}

[settings]
height_scale = {{heightScale}}
""";
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-runtime-bootstrap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string FullPath(params string[] segments)
    {
        return Path.GetFullPath(Path.Combine(segments));
    }

    private static void AssertThrowsInvalidData(Action action, string message)
    {
        TestHarness.AssertThrows<InvalidDataException>(action, message);
    }
}
