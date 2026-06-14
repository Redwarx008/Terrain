namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorPendingResourceWorkflowTests
{
    public static void RunAll()
    {
        TestHarness.Run("terrain manager has a dedicated pending-heightmap branch", TerrainManagerHasDedicatedPendingHeightmapBranch);
        TestHarness.Run("terrain manager keeps config loading ahead of pending terrain short-circuit", TerrainManagerKeepsConfigLoadingAheadOfPendingTerrainShortCircuit);
    }

    private static void TerrainManagerHasDedicatedPendingHeightmapBranch()
    {
        string source = File.ReadAllText(GetTerrainManagerSourcePath());

        TestHarness.Assert(source.Contains("if (session.HasPendingHeightmap)", StringComparison.Ordinal), "pending heightmap branch should exist");
        TestHarness.Assert(source.Contains("RemoveCurrentTerrain();", StringComparison.Ordinal), "pending branch should clear previous terrain");
        TestHarness.Assert(source.Contains("Log.Error(lastLoadError);", StringComparison.Ordinal), "pending branch should emit an error log");
    }

    private static void TerrainManagerKeepsConfigLoadingAheadOfPendingTerrainShortCircuit()
    {
        string source = File.ReadAllText(GetTerrainManagerSourcePath());

        int descriptorRead = source.IndexOf("RuntimeMaterialDescriptorReader.ReadFrom", StringComparison.Ordinal);
        int biomeRead = source.IndexOf("RuntimeBiomeSettingsReader.ReadFrom", StringComparison.Ordinal);
        int pendingBranch = source.IndexOf("if (session.HasPendingHeightmap)", StringComparison.Ordinal);

        TestHarness.Assert(descriptorRead >= 0, "descriptor read should remain present");
        TestHarness.Assert(biomeRead >= 0, "biome settings read should remain present");
        TestHarness.Assert(pendingBranch >= 0, "pending branch should remain present");
        TestHarness.Assert(descriptorRead < pendingBranch, "descriptor should load before pending branch exits");
        TestHarness.Assert(biomeRead < pendingBranch, "biome settings should load before pending branch exits");
    }

    private static string GetTerrainManagerSourcePath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "Terrain.Editor",
            "Services",
            "TerrainManager.cs"));
    }
}
