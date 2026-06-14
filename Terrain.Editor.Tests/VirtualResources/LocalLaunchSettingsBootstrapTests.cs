using System.Text.Json;
using Terrain.Editor.Services.Resources;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class LocalLaunchSettingsBootstrapTests
{
    public static void RunAll()
    {
        TestHarness.Run("editor bootstrap auto-creates exe launch settings and keeps missing runtime targets writable", EditorBootstrapAutoCreatesExeLaunchSettingsAndKeepsMissingRuntimeTargetsWritable);
        TestHarness.Run("editor bootstrap auto-creates missing authoring tomls", EditorBootstrapAutoCreatesMissingAuthoringTomls);
        TestHarness.Run("editor bootstrap keeps missing heightmap as pending resource", EditorBootstrapKeepsMissingHeightmapAsPendingResource);
        TestHarness.Run("editor bootstrap resolves mod override from exe launch settings", EditorBootstrapResolvesModOverrideFromExeLaunchSettings);
        TestHarness.Run("runtime bootstrap resolves sibling game resources from exe launch settings", RuntimeBootstrapResolvesSiblingGameResourcesFromExeLaunchSettings);
        TestHarness.Run("runtime bootstrap resolves mod override from exe launch settings", RuntimeBootstrapResolvesModOverrideFromExeLaunchSettings);
        TestHarness.Run("runtime bootstrap rejects mod root that equals game root", RuntimeBootstrapRejectsModRootThatEqualsGameRoot);
        TestHarness.Run("runtime bootstrap rejects mod root under game root", RuntimeBootstrapRejectsModRootUnderGameRoot);
    }

    private static void EditorBootstrapAutoCreatesExeLaunchSettingsAndKeepsMissingRuntimeTargetsWritable()
    {
        (string _, string appRoot, string gameRoot) = CreateWorkspaceWithBaseMap();

        EditorResourceSession session = new EditorBootstrapService().LoadCurrentSession(appRoot);

        TestHarness.Assert(File.Exists(Path.Combine(appRoot, "LaunchSetting.json")), "LaunchSetting.json should be created next to the exe");
        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(gameRoot, "map_data", "default.toml")), session.MapDefinition.ResolvedPath, "map definition should come from base game");
        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(gameRoot, "map_data", "heightmap.png")), session.Heightmap.ResolvedPath, "heightmap should come from base game");
        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(gameRoot, "map_data", "terrain.terrain")), session.TerrainData.ResolvedPath, "terrain export target should stay in base game");
        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(gameRoot, "map_data", "biome_mask.png")), session.BiomeMask.ResolvedPath, "biome mask target should stay in base game");
        TestHarness.Assert(session.TerrainData.IsWritable, "missing terrain target should remain writable");
        TestHarness.Assert(session.BiomeMask.IsWritable, "missing biome mask target should remain writable");
    }

    private static void EditorBootstrapAutoCreatesMissingAuthoringTomls()
    {
        (string _, string appRoot, string gameRoot) = CreateWorkspaceWithBaseMap(includeAuthoringTomls: false);

        EditorResourceSession session = new EditorBootstrapService().LoadCurrentSession(appRoot);

        TestHarness.Assert(File.Exists(Path.Combine(gameRoot, "map_data", "default.toml")), "default.toml should be generated");
        TestHarness.Assert(File.Exists(Path.Combine(gameRoot, "map_data", "biome_settings.toml")), "biome_settings.toml should be generated");
        TestHarness.Assert(File.Exists(Path.Combine(gameRoot, "map_data", "materials", "descriptor.toml")), "descriptor.toml should be generated");
        TestHarness.Assert(!session.HasPendingHeightmap, "existing base heightmap should not be pending");
    }

    private static void EditorBootstrapKeepsMissingHeightmapAsPendingResource()
    {
        (string _, string appRoot, string gameRoot) = CreateWorkspaceWithBaseMap(includeAuthoringTomls: true);
        File.Delete(Path.Combine(gameRoot, "map_data", "heightmap.png"));

        EditorResourceSession session = new EditorBootstrapService().LoadCurrentSession(appRoot);

        TestHarness.Assert(session.HasPendingHeightmap, "missing heightmap should become a pending resource");
        TestHarness.AssertEqual(
            Path.GetFullPath(Path.Combine(gameRoot, "map_data", "heightmap.png")),
            session.PendingHeightmapPath,
            "pending heightmap path");
        TestHarness.AssertEqual(
            Path.GetFullPath(Path.Combine(gameRoot, "map_data", "heightmap.png")),
            session.Heightmap.ResolvedPath,
            "heightmap target should still resolve to the writable authoring path");
    }

    private static void EditorBootstrapResolvesModOverrideFromExeLaunchSettings()
    {
        (string root, string appRoot, string _) = CreateWorkspaceWithBaseMap();
        string modRoot = Path.Combine(root, "mods", "example_mod");
        Directory.CreateDirectory(Path.Combine(modRoot, "map_data"));

        File.WriteAllText(Path.Combine(modRoot, "map_data", "default.toml"), """
version = 1

[terrain]
heightmap = "modded-heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");
        File.WriteAllText(Path.Combine(modRoot, "map_data", "modded-heightmap.png"), "mod height");
        File.WriteAllText(Path.Combine(appRoot, "LaunchSetting.json"), JsonSerializer.Serialize(new
        {
            version = 1,
            mods = new[]
            {
                new { id = "example_mod", root = modRoot, enabled = true },
            },
        }));

        EditorResourceSession session = new EditorBootstrapService().LoadCurrentSession(appRoot);

        TestHarness.AssertEqual("example_mod", session.MapDefinition.SourceLayerId, "map definition should come from the mod layer");
        TestHarness.AssertEqual("example_mod", session.Heightmap.SourceLayerId, "heightmap should come from the mod layer");
    }

    private static void RuntimeBootstrapResolvesSiblingGameResourcesFromExeLaunchSettings()
    {
        (string _, string appRoot, string gameRoot) = CreateWorkspaceWithBaseMap(includeRuntimeResources: true);

        GameResourceResolver resolver = GameResourceResolverBootstrap.CreateForAppDirectory(appRoot);
        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(resolver).Load();

        TestHarness.Assert(File.Exists(Path.Combine(appRoot, "LaunchSetting.json")), "LaunchSetting.json should be created next to the exe");
        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(gameRoot, "map_data", "terrain.terrain")), bundle.TerrainDataPath, "terrain data should come from sibling game");
        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(gameRoot, "map_data", "biome_mask.png")), bundle.BiomeMaskPath, "biome mask should come from sibling game");
        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(gameRoot, "map_data", "biome_settings.toml")), bundle.BiomeSettingsPath, "biome settings should come from sibling game");
        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(gameRoot, "map_data", "materials", "descriptor.toml")), bundle.MaterialDescriptorPath, "material descriptor should come from sibling game");
    }

    private static void RuntimeBootstrapResolvesModOverrideFromExeLaunchSettings()
    {
        (string root, string appRoot, string _) = CreateWorkspaceWithBaseMap(includeRuntimeResources: true);
        string modRoot = Path.Combine(root, "mods", "example_mod");
        Directory.CreateDirectory(Path.Combine(modRoot, "map_data"));
        File.WriteAllText(Path.Combine(modRoot, "map_data", "terrain.terrain"), "mod terrain");
        File.WriteAllText(Path.Combine(appRoot, "LaunchSetting.json"), JsonSerializer.Serialize(new
        {
            version = 1,
            mods = new[]
            {
                new { id = "example_mod", root = modRoot, enabled = true },
            },
        }));

        GameResourceResolver resolver = GameResourceResolverBootstrap.CreateForAppDirectory(appRoot);
        TerrainRuntimeResourceBundle bundle = new GameRuntimeResourceBootstrap(resolver).Load();

        TestHarness.AssertEqual(Path.GetFullPath(Path.Combine(modRoot, "map_data", "terrain.terrain")), bundle.TerrainDataPath, "terrain data should come from the enabled mod layer");
    }

    private static void RuntimeBootstrapRejectsModRootThatEqualsGameRoot()
    {
        (string _, string appRoot, string gameRoot) = CreateWorkspaceWithBaseMap(includeRuntimeResources: true);
        File.WriteAllText(Path.Combine(appRoot, "LaunchSetting.json"), JsonSerializer.Serialize(new
        {
            version = 1,
            mods = new[]
            {
                new { id = "invalid_mod", root = gameRoot, enabled = true },
            },
        }));

        InvalidDataException ex = TestHarness.AssertThrows<InvalidDataException>(
            () => GameResourceResolverBootstrap.CreateForAppDirectory(appRoot),
            "mod root equal to game root should be rejected");

        TestHarness.Assert(
            ex.Message.Contains("game root", StringComparison.OrdinalIgnoreCase),
            $"error message should mention game root. Actual: {ex.Message}");
    }

    private static void RuntimeBootstrapRejectsModRootUnderGameRoot()
    {
        (string _, string appRoot, string gameRoot) = CreateWorkspaceWithBaseMap(includeRuntimeResources: true);
        string nestedModRoot = Path.Combine(gameRoot, "mods", "invalid_mod");
        Directory.CreateDirectory(Path.Combine(nestedModRoot, "map_data"));
        File.WriteAllText(Path.Combine(appRoot, "LaunchSetting.json"), JsonSerializer.Serialize(new
        {
            version = 1,
            mods = new[]
            {
                new { id = "invalid_mod", root = nestedModRoot, enabled = true },
            },
        }));

        InvalidDataException ex = TestHarness.AssertThrows<InvalidDataException>(
            () => GameResourceResolverBootstrap.CreateForAppDirectory(appRoot),
            "mod root under game root should be rejected");

        TestHarness.Assert(
            ex.Message.Contains("game root", StringComparison.OrdinalIgnoreCase),
            $"error message should mention game root. Actual: {ex.Message}");
    }

    private static (string Root, string AppRoot, string GameRoot) CreateWorkspaceWithBaseMap(
        bool includeRuntimeResources = false,
        bool includeAuthoringTomls = true)
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-local-launch-settings-tests", Guid.NewGuid().ToString("N"));
        string gameRoot = Path.Combine(root, "game");
        string appRoot = Path.Combine(root, "bin", "Debug", "net8.0");
        Directory.CreateDirectory(Path.Combine(gameRoot, "map_data", "materials"));
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(root, "Terrain.sln"), string.Empty);
        File.WriteAllText(Path.Combine(gameRoot, "map_data", "heightmap.png"), "base height");
        if (includeAuthoringTomls)
        {
            File.WriteAllText(Path.Combine(gameRoot, "map_data", "default.toml"), """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");
            File.WriteAllText(Path.Combine(gameRoot, "map_data", "biome_settings.toml"), """
version = 1

biomes = []
layers = []
modifiers = []
""");
            File.WriteAllText(Path.Combine(gameRoot, "map_data", "materials", "descriptor.toml"), """
version = 1
materials = []
""");
        }
        File.WriteAllText(Path.Combine(gameRoot, "map_data", "materials", "plains_01_diffuse.dds"), "base diffuse");
        File.WriteAllText(Path.Combine(gameRoot, "map_data", "materials", "plains_01_normal.dds"), "base normal");
        File.WriteAllText(Path.Combine(gameRoot, "map_data", "materials", "plains_01_properties.dds"), "base properties");

        if (includeRuntimeResources)
        {
            File.WriteAllText(Path.Combine(gameRoot, "map_data", "terrain.terrain"), "base terrain");
            File.WriteAllText(Path.Combine(gameRoot, "map_data", "biome_mask.png"), "base biome mask");
        }

        return (root, appRoot, gameRoot);
    }
}
