using Terrain.Editor.Services.Resources;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorMissingMaterialWorkflowTests
{
    public static void RunAll()
    {
        TestHarness.Run("recovery reports missing properties texture as non-blocking fallback", RecoveryReportsMissingPropertiesTextureAsNonBlockingFallback);
        TestHarness.Run("recovery reports absolute missing texture paths", RecoveryReportsAbsoluteMissingTexturePaths);
        TestHarness.Run("recovery state blocks save and export when biome references missing material id", RecoveryStateBlocksSaveAndExportWhenBiomeReferencesMissingMaterialId);
    }

    private static void RecoveryReportsMissingPropertiesTextureAsNonBlockingFallback()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "game", "map_data", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);

        var descriptor = new RuntimeMaterialDescriptor();
        descriptor.Materials.Add(new RuntimeMaterialEntry
        {
            Id = "grass",
            Index = 5,
            Name = "Grass",
            PropertiesPath = "grass_p.png",
        });

        RuntimeBiomeSettings settings = CreateSingleMaterialSettings("grass");

        EditorMaterialRecoveryResult result = new EditorMaterialRecoveryService().Recover(
            descriptor,
            descriptorPath,
            settings,
            ResolveMissingMaterialTexture);

        EditorMaterialLoadIssue issue = result.LoadState.Issues.Single(entry => entry.Kind == EditorMaterialLoadIssueKind.MissingPropertiesTexture);
        TestHarness.AssertEqual("grass", issue.MaterialId, "properties fallback issue should target the material");
        TestHarness.Assert(result.LoadState.HasTextureFallbacks, "missing properties should count as texture fallback");
        TestHarness.Assert(!result.LoadState.HasBlockingMissingMaterialIds, "missing properties should remain non-blocking");
    }

    private static void RecoveryReportsAbsoluteMissingTexturePaths()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "game", "map_data", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);

        var descriptor = new RuntimeMaterialDescriptor();
        descriptor.Materials.Add(new RuntimeMaterialEntry
        {
            Id = "grass",
            Index = 5,
            Name = "Grass",
            AlbedoPath = "missing_grass.png",
            NormalPath = "missing_grass_n.png",
        });

        RuntimeBiomeSettings settings = CreateSingleMaterialSettings("grass");

        EditorMaterialRecoveryResult result = new EditorMaterialRecoveryService().Recover(
            descriptor,
            descriptorPath,
            settings,
            ResolveMissingMaterialTexture);

        string expectedAlbedoPath = Path.Combine(root, "game", "map_data", "materials", "missing_grass.png");
        string expectedNormalPath = Path.Combine(root, "game", "map_data", "materials", "missing_grass_n.png");

        EditorMaterialLoadIssue albedoIssue = result.LoadState.Issues.Single(entry => entry.Kind == EditorMaterialLoadIssueKind.MissingAlbedoTexture);
        EditorMaterialLoadIssue normalIssue = result.LoadState.Issues.Single(entry => entry.Kind == EditorMaterialLoadIssueKind.MissingNormalTexture);

        TestHarness.AssertEqual(expectedAlbedoPath, albedoIssue.Path, "missing albedo issue should report the expected absolute path");
        TestHarness.AssertEqual(expectedNormalPath, normalIssue.Path, "missing normal issue should report the expected absolute path");
        TestHarness.Assert(albedoIssue.Message.Contains(expectedAlbedoPath, StringComparison.Ordinal), "albedo message should include absolute path");
        TestHarness.Assert(normalIssue.Message.Contains(expectedNormalPath, StringComparison.Ordinal), "normal message should include absolute path");
    }

    private static void RecoveryStateBlocksSaveAndExportWhenBiomeReferencesMissingMaterialId()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "game", "map_data", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);

        var descriptor = new RuntimeMaterialDescriptor();
        RuntimeBiomeSettings settings = CreateSingleMaterialSettings("rock");

        EditorMaterialRecoveryResult result = new EditorMaterialRecoveryService().Recover(
            descriptor,
            descriptorPath,
            settings,
            ResolveMissingMaterialTexture);

        EditorResourceSession session = CreateSession(root);
        session.ApplyMaterialLoadState(result.LoadState);

        TestHarness.Assert(!session.CanSaveAuthoringResources, "missing material id should block save");
        TestHarness.Assert(!session.CanExportTerrainData, "missing material id should block export");
    }

    private static RuntimeBiomeSettings CreateSingleMaterialSettings(string materialId)
    {
        var settings = new RuntimeBiomeSettings();
        settings.Biomes.Add(new RuntimeBiomeEntry { Id = 1, Name = "Temperate" });
        settings.Layers.Add(new RuntimeBiomeLayerEntry
        {
            Id = 10,
            BiomeId = 1,
            Name = "Layer",
            MaterialId = materialId,
            Priority = 0,
            Enabled = true,
            Visible = true,
        });
        return settings;
    }

    private static EditorResourceSession CreateSession(string root)
    {
        static ResolvedGameResource Resource(string virtualPath, string path)
        {
            return new ResolvedGameResource(virtualPath, path, "mod", IsWritable: true, HasLowerPriorityFallback: false);
        }

        return new EditorResourceSession(
            Resource("map_data/default.toml", Path.Combine(root, "game", "map_data", "default.toml")),
            Resource("map_data/heightmap.png", Path.Combine(root, "game", "map_data", "heightmap.png")),
            Resource("map_data/terrain.terrain", Path.Combine(root, "game", "map_data", "terrain.terrain")),
            Resource("map_data/biome_mask.png", Path.Combine(root, "game", "map_data", "biome_mask.png")),
            Resource("map_data/biome_settings.toml", Path.Combine(root, "game", "map_data", "biome_settings.toml")),
            Resource("map_data/materials/descriptor.toml", Path.Combine(root, "game", "map_data", "materials", "descriptor.toml")),
            new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 100.0f,
            });
    }

    private static string? ResolveMissingMaterialTexture(string virtualPath)
    {
        return null;
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-editor-missing-material-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
