namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorPendingResourceWorkflowTests
{
    public static void RunAll()
    {
        TestHarness.Run("terrain manager has a dedicated pending-heightmap branch", TerrainManagerHasDedicatedPendingHeightmapBranch);
        TestHarness.Run("terrain manager pending-heightmap branch keeps required side effects in order", TerrainManagerPendingHeightmapBranchKeepsRequiredSideEffectsInOrder);
        TestHarness.Run("terrain manager keeps config loading ahead of pending terrain short-circuit", TerrainManagerKeepsConfigLoadingAheadOfPendingTerrainShortCircuit);
        TestHarness.Run("terrain manager normal path loads terrain before biome mask and optional rivers", TerrainManagerNormalPathLoadsTerrainBeforeBiomeMaskAndOptionalRivers);
    }

    private static void TerrainManagerHasDedicatedPendingHeightmapBranch()
    {
        string source = GetTerrainManagerSource();

        TestHarness.Assert(source.Contains("if (session.HasPendingHeightmap)", StringComparison.Ordinal), "pending heightmap branch should exist");
        TestHarness.Assert(source.Contains("RemoveCurrentTerrain();", StringComparison.Ordinal), "pending branch should clear previous terrain");
        TestHarness.Assert(source.Contains("Log.Error(lastLoadError);", StringComparison.Ordinal), "pending branch should emit an error log");
    }

    private static void TerrainManagerPendingHeightmapBranchKeepsRequiredSideEffectsInOrder()
    {
        string pendingBranch = GetBlockAfter("if (session.HasPendingHeightmap)");

        int removeTerrain = pendingBranch.IndexOf("RemoveCurrentTerrain();", StringComparison.Ordinal);
        int setError = pendingBranch.IndexOf("lastLoadError = $\"Terrain workspace heightmap is missing: {session.Heightmap.ResolvedPath}\";", StringComparison.Ordinal);
        int logError = pendingBranch.IndexOf("Log.Error(lastLoadError);", StringComparison.Ordinal);
        int pendingRiversBranch = pendingBranch.IndexOf("if (session.Rivers is { } pendingRivers)", StringComparison.Ordinal);
        int loadPendingRivers = pendingBranch.IndexOf("LoadRiverMap(pendingRivers.ResolvedPath, markDirty: false);", StringComparison.Ordinal);
        int texturesEvent = pendingBranch.IndexOf("MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);", StringComparison.Ordinal);
        int returnEmpty = pendingBranch.IndexOf("return new List<EditorTerrainEntity>();", StringComparison.Ordinal);

        TestHarness.Assert(removeTerrain >= 0, "pending branch should clear previous terrain");
        TestHarness.Assert(setError >= 0, "pending branch should set the missing-heightmap load error");
        TestHarness.Assert(logError >= 0, "pending branch should log the pending-heightmap error");
        TestHarness.Assert(pendingRiversBranch >= 0, "pending branch should keep optional rivers handling");
        TestHarness.Assert(loadPendingRivers >= 0, "pending branch should optionally load rivers");
        TestHarness.Assert(texturesEvent >= 0, "pending branch should still request material textures");
        TestHarness.Assert(returnEmpty >= 0, "pending branch should return an empty terrain list");
        TestHarness.Assert(removeTerrain < setError, "pending branch should clear terrain before reporting the missing heightmap");
        TestHarness.Assert(setError < logError, "pending branch should assign the missing-heightmap error before logging it");
        TestHarness.Assert(logError < pendingRiversBranch, "pending branch should log before optional rivers handling");
        TestHarness.Assert(pendingRiversBranch < loadPendingRivers, "pending branch should guard optional river loading");
        TestHarness.Assert(loadPendingRivers < texturesEvent, "pending branch should load rivers before requesting textures");
        TestHarness.Assert(texturesEvent < returnEmpty, "pending branch should request textures before returning an empty list");
    }

    private static void TerrainManagerKeepsConfigLoadingAheadOfPendingTerrainShortCircuit()
    {
        string source = GetTerrainManagerSource();

        int descriptorRead = source.IndexOf("RuntimeMaterialDescriptorReader.ReadFrom", StringComparison.Ordinal);
        int biomeRead = source.IndexOf("RuntimeBiomeSettingsReader.ReadFrom", StringComparison.Ordinal);
        int pendingBranch = source.IndexOf("if (session.HasPendingHeightmap)", StringComparison.Ordinal);

        TestHarness.Assert(descriptorRead >= 0, "descriptor read should remain present");
        TestHarness.Assert(biomeRead >= 0, "biome settings read should remain present");
        TestHarness.Assert(pendingBranch >= 0, "pending branch should remain present");
        TestHarness.Assert(descriptorRead < pendingBranch, "descriptor should load before pending branch exits");
        TestHarness.Assert(biomeRead < pendingBranch, "biome settings should load before pending branch exits");
    }

    private static void TerrainManagerNormalPathLoadsTerrainBeforeBiomeMaskAndOptionalRivers()
    {
        string methodBody = GetLoadFromResourceSessionBody();

        int loadTerrain = methodBody.IndexOf("List<EditorTerrainEntity> entities = await LoadTerrainAsync(session.Heightmap.ResolvedPath);", StringComparison.Ordinal);
        int noTerrainReturn = methodBody.IndexOf("if (entities.Count == 0)", StringComparison.Ordinal);
        int loadBiomeMask = methodBody.IndexOf("LoadBiomeMask(session.BiomeMask.ResolvedPath, markDirty: false);", StringComparison.Ordinal);
        int riversBranch = methodBody.IndexOf("if (session.Rivers is { } rivers)", StringComparison.Ordinal);
        int loadRivers = methodBody.IndexOf("LoadRiverMap(rivers.ResolvedPath, markDirty: false);", StringComparison.Ordinal);

        TestHarness.Assert(loadTerrain >= 0, "normal path should still load terrain");
        TestHarness.Assert(noTerrainReturn >= 0, "normal path should still bail out when terrain loading produces no entities");
        TestHarness.Assert(loadBiomeMask >= 0, "normal path should load biome mask");
        TestHarness.Assert(riversBranch >= 0, "normal path should keep optional rivers handling");
        TestHarness.Assert(loadRivers >= 0, "normal path should optionally load rivers");
        TestHarness.Assert(loadTerrain < noTerrainReturn, "normal path should check terrain results after loading terrain");
        TestHarness.Assert(noTerrainReturn < loadBiomeMask, "normal path should only load biome mask after terrain has loaded successfully");
        TestHarness.Assert(loadBiomeMask < riversBranch, "normal path should load biome mask before optional rivers handling");
        TestHarness.Assert(riversBranch < loadRivers, "normal path river loading should remain guarded by the optional branch");
    }

    private static string GetTerrainManagerSource()
    {
        return File.ReadAllText(GetTerrainManagerSourcePath());
    }

    private static string GetLoadFromResourceSessionBody()
    {
        return GetBlockAfter("public async Task<List<EditorTerrainEntity>> LoadFromResourceSession(EditorResourceSession session)");
    }

    private static string GetBlockAfter(string marker)
    {
        string source = GetTerrainManagerSource();
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        TestHarness.Assert(markerIndex >= 0, $"marker should exist: {marker}");

        int openBrace = source.IndexOf('{', markerIndex);
        TestHarness.Assert(openBrace >= 0, $"opening brace should exist after marker: {marker}");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        throw new InvalidOperationException($"closing brace should exist after marker: {marker}");
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
