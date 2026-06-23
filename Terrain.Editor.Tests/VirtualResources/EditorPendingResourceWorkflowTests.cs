namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorPendingResourceWorkflowTests
{
    public static void RunAll()
    {
        TestHarness.Run("terrain manager has a dedicated pending-heightmap branch", TerrainManagerHasDedicatedPendingHeightmapBranch);
        TestHarness.Run("terrain manager pending-heightmap branch keeps required side effects in order", TerrainManagerPendingHeightmapBranchKeepsRequiredSideEffectsInOrder);
        TestHarness.Run("terrain manager keeps config loading ahead of pending terrain short-circuit", TerrainManagerKeepsConfigLoadingAheadOfPendingTerrainShortCircuit);
        TestHarness.Run("terrain manager failed terrain loads clear stale terrain and rivers before returning empty", TerrainManagerFailedTerrainLoadsClearStaleTerrainAndRiversBeforeReturningEmpty);
        TestHarness.Run("terrain manager normal path loads terrain before biome mask and optional rivers", TerrainManagerNormalPathLoadsTerrainBeforeBiomeMaskAndOptionalRivers);
        TestHarness.Run("editor shell keeps pending sessions instead of treating them as load failure", EditorShellKeepsPendingSessionsInsteadOfTreatingThemAsLoadFailure);
        TestHarness.Run("editor shell blocks save and export when heightmap is pending", EditorShellBlocksSaveAndExportWhenHeightmapIsPending);
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
        string pendingBranch = GetBlockAfter(GetTerrainManagerSource(), "if (session.HasPendingHeightmap)");

        int removeTerrain = pendingBranch.IndexOf("RemoveCurrentTerrain();", StringComparison.Ordinal);
        int setError = pendingBranch.IndexOf("lastLoadError = $\"Terrain workspace heightmap is missing: {session.Heightmap.ResolvedPath}\";", StringComparison.Ordinal);
        int logError = pendingBranch.IndexOf("Log.Error(lastLoadError);", StringComparison.Ordinal);
        int pendingRiversBranch = pendingBranch.IndexOf("if (session.Rivers is { } pendingRivers)", StringComparison.Ordinal);
        int loadPendingRivers = pendingBranch.IndexOf("LoadRiverMap(pendingRivers.ResolvedPath);", StringComparison.Ordinal);
        int clearPendingRivers = pendingBranch.IndexOf("ClearRiverMap();", StringComparison.Ordinal);
        int texturesEvent = pendingBranch.IndexOf("MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);", StringComparison.Ordinal);
        int returnEmpty = pendingBranch.IndexOf("return new List<EditorTerrainEntity>();", StringComparison.Ordinal);

        TestHarness.Assert(removeTerrain >= 0, "pending branch should clear previous terrain");
        TestHarness.Assert(setError >= 0, "pending branch should set the missing-heightmap load error");
        TestHarness.Assert(logError >= 0, "pending branch should log the pending-heightmap error");
        TestHarness.Assert(pendingRiversBranch >= 0, "pending branch should keep optional rivers handling");
        TestHarness.Assert(loadPendingRivers >= 0, "pending branch should optionally load rivers");
        TestHarness.Assert(clearPendingRivers >= 0, "pending branch should clear stale river state when no rivers resource exists");
        TestHarness.Assert(texturesEvent >= 0, "pending branch should still request material textures");
        TestHarness.Assert(returnEmpty >= 0, "pending branch should return an empty terrain list");
        TestHarness.Assert(removeTerrain < setError, "pending branch should clear terrain before reporting the missing heightmap");
        TestHarness.Assert(setError < logError, "pending branch should assign the missing-heightmap error before logging it");
        TestHarness.Assert(logError < pendingRiversBranch, "pending branch should log before optional rivers handling");
        TestHarness.Assert(pendingRiversBranch < loadPendingRivers, "pending branch should guard optional river loading");
        TestHarness.Assert(pendingRiversBranch < clearPendingRivers, "pending branch should decide river handling before clearing stale river state");
        TestHarness.Assert(loadPendingRivers < texturesEvent, "pending branch should load rivers before requesting textures");
        TestHarness.Assert(clearPendingRivers < texturesEvent, "pending branch should clear stale river state before requesting textures");
        TestHarness.Assert(texturesEvent < returnEmpty, "pending branch should request textures before returning an empty list");
    }

    private static void TerrainManagerKeepsConfigLoadingAheadOfPendingTerrainShortCircuit()
    {
        string methodBody = GetLoadFromResourceSessionBody();

        int descriptorRead = methodBody.IndexOf("RuntimeMaterialDescriptorReader.ReadFrom", StringComparison.Ordinal);
        int applyDescriptor = methodBody.IndexOf("MaterialSlotManager.Instance.ApplyDescriptor", StringComparison.Ordinal);
        int biomeRead = methodBody.IndexOf("RuntimeBiomeSettingsReader.ReadFrom", StringComparison.Ordinal);
        int applyBiomeSettings = methodBody.IndexOf("BiomeRuleService.Instance.ApplyRuntimeSettings", StringComparison.Ordinal);
        int pendingBranch = methodBody.IndexOf("if (session.HasPendingHeightmap)", StringComparison.Ordinal);

        TestHarness.Assert(descriptorRead >= 0, "descriptor read should remain present");
        TestHarness.Assert(applyDescriptor >= 0, "descriptor apply should remain present");
        TestHarness.Assert(biomeRead >= 0, "biome settings read should remain present");
        TestHarness.Assert(applyBiomeSettings >= 0, "biome settings apply should remain present");
        TestHarness.Assert(pendingBranch >= 0, "pending branch should remain present");
        TestHarness.Assert(descriptorRead < pendingBranch, "descriptor should load before pending branch exits");
        TestHarness.Assert(applyDescriptor < pendingBranch, "descriptor should apply before pending branch exits");
        TestHarness.Assert(biomeRead < pendingBranch, "biome settings should load before pending branch exits");
        TestHarness.Assert(applyBiomeSettings < pendingBranch, "biome settings should apply before pending branch exits");
    }

    private static void TerrainManagerNormalPathLoadsTerrainBeforeBiomeMaskAndOptionalRivers()
    {
        string methodBody = GetLoadFromResourceSessionBody();

        int loadTerrain = methodBody.IndexOf("List<EditorTerrainEntity> entities = await LoadTerrainAsync(session.Heightmap.ResolvedPath);", StringComparison.Ordinal);
        int noTerrainReturn = methodBody.IndexOf("if (entities.Count == 0)", StringComparison.Ordinal);
        int loadBiomeMask = methodBody.IndexOf("LoadBiomeMask(session.BiomeMask.ResolvedPath, markDirty: false);", StringComparison.Ordinal);
        int riversBranch = methodBody.IndexOf("if (session.Rivers is { } rivers)", loadBiomeMask, StringComparison.Ordinal);
        int loadRivers = methodBody.IndexOf("LoadRiverMap(rivers.ResolvedPath);", loadBiomeMask, StringComparison.Ordinal);
        int clearRivers = methodBody.IndexOf("ClearRiverMap();", loadBiomeMask, StringComparison.Ordinal);
        int texturesEvent = methodBody.IndexOf("MaterialTexturesLoadRequired?.Invoke(this, EventArgs.Empty);", loadBiomeMask, StringComparison.Ordinal);

        TestHarness.Assert(loadTerrain >= 0, "normal path should still load terrain");
        TestHarness.Assert(noTerrainReturn >= 0, "normal path should still bail out when terrain loading produces no entities");
        TestHarness.Assert(loadBiomeMask >= 0, "normal path should load biome mask");
        TestHarness.Assert(riversBranch >= 0, "normal path should keep optional rivers handling");
        TestHarness.Assert(loadRivers >= 0, "normal path should optionally load rivers");
        TestHarness.Assert(clearRivers >= 0, "normal path should clear stale river state when no rivers resource exists");
        TestHarness.Assert(texturesEvent >= 0, "normal path should still request material textures");
        TestHarness.Assert(loadTerrain < noTerrainReturn, "normal path should check terrain results after loading terrain");
        TestHarness.Assert(noTerrainReturn < loadBiomeMask, "normal path should only load biome mask after terrain has loaded successfully");
        TestHarness.Assert(loadBiomeMask < riversBranch, "normal path should load biome mask before optional rivers handling");
        TestHarness.Assert(riversBranch < loadRivers, "normal path river loading should remain guarded by the optional branch");
        TestHarness.Assert(riversBranch < clearRivers, "normal path should decide river handling before clearing stale river state");
        TestHarness.Assert(loadRivers < texturesEvent, "normal path should load rivers before requesting textures");
        TestHarness.Assert(clearRivers < texturesEvent, "normal path should clear stale river state before requesting textures");
    }

    private static void TerrainManagerFailedTerrainLoadsClearStaleTerrainAndRiversBeforeReturningEmpty()
    {
        string methodBody = GetLoadFromResourceSessionBody();

        int noTerrainReturn = methodBody.IndexOf("if (entities.Count == 0)", StringComparison.Ordinal);
        int loadBiomeMask = methodBody.IndexOf("LoadBiomeMask(session.BiomeMask.ResolvedPath, markDirty: false);", StringComparison.Ordinal);

        TestHarness.Assert(noTerrainReturn >= 0, "failed terrain-load guard should exist");
        TestHarness.Assert(loadBiomeMask >= 0, "normal-path biome mask load should remain present");
        TestHarness.Assert(noTerrainReturn < loadBiomeMask, "failed terrain-load guard should remain ahead of normal-path biome mask load");

        string failureGuard = methodBody.Substring(noTerrainReturn, loadBiomeMask - noTerrainReturn);
        int removeTerrain = failureGuard.IndexOf("RemoveCurrentTerrain();", StringComparison.Ordinal);
        int clearRivers = failureGuard.IndexOf("ClearRiverMap();", StringComparison.Ordinal);
        int returnEntities = failureGuard.IndexOf("return entities;", StringComparison.Ordinal);

        TestHarness.Assert(removeTerrain >= 0, "failed terrain-load guard should clear stale terrain");
        TestHarness.Assert(clearRivers >= 0, "failed terrain-load guard should clear stale river state");
        TestHarness.Assert(returnEntities >= 0, "failed terrain-load guard should still return the empty entity list");
        TestHarness.Assert(removeTerrain < clearRivers, "failed terrain-load guard should clear terrain before rivers");
        TestHarness.Assert(clearRivers < returnEntities, "failed terrain-load guard should clear stale river state before returning");
    }

    private static void EditorShellKeepsPendingSessionsInsteadOfTreatingThemAsLoadFailure()
    {
        string pendingBranch = GetBlockAfter(GetEditorShellViewModelSource(), "if (session.HasPendingHeightmap)");
        string failedEntitiesBranch = GetBlockAfter(GetEditorShellViewModelSource(), "if (entities.Count == 0)");

        TestHarness.Assert(pendingBranch.Contains("_resourceSession = session;", StringComparison.Ordinal), "pending branch should keep the loaded session");
        TestHarness.Assert(pendingBranch.Contains("SyncSettingsFromTerrainManager();", StringComparison.Ordinal), "pending branch should sync settings");
        TestHarness.Assert(pendingBranch.Contains("EditorDirtyState.Instance.ClearDirty();", StringComparison.Ordinal), "pending branch should clear dirty state");
        TestHarness.Assert(pendingBranch.Contains("RefreshAssetItems();", StringComparison.Ordinal), "pending branch should refresh asset items");
        TestHarness.Assert(pendingBranch.Contains("Biome.NotifyMaterialPreviewsChanged();", StringComparison.Ordinal), "pending branch should refresh biome previews");
        TestHarness.Assert(pendingBranch.Contains("RefreshProjectState();", StringComparison.Ordinal), "pending branch should refresh project state");
        TestHarness.Assert(pendingBranch.Contains("AddConsole(\"Error\", $\"Terrain workspace heightmap is missing: {session.PendingHeightmapPath}\");", StringComparison.Ordinal), "pending branch should log missing heightmap");
        TestHarness.Assert(pendingBranch.Contains("AddConsole(\"Warning\", \"Terrain workspace loaded with pending resources. Add the missing heightmap before save/export.\");", StringComparison.Ordinal), "pending branch should log pending warning");

        TestHarness.Assert(!failedEntitiesBranch.Contains("_resourceSession = session;", StringComparison.Ordinal), "failed-entities branch should not keep the session");
        TestHarness.Assert(!failedEntitiesBranch.Contains("SyncSettingsFromTerrainManager();", StringComparison.Ordinal), "failed-entities branch should not sync settings");
        TestHarness.Assert(!failedEntitiesBranch.Contains("RefreshAssetItems();", StringComparison.Ordinal), "failed-entities branch should not refresh asset items");
        TestHarness.Assert(!failedEntitiesBranch.Contains("RefreshProjectState();", StringComparison.Ordinal), "failed-entities branch should not refresh project state");
    }

    private static void EditorShellBlocksSaveAndExportWhenHeightmapIsPending()
    {
        string saveBody = GetEditorShellMethodBody("private async Task Save()");
        string exportBody = GetEditorShellMethodBody("private async Task ExportTerrain()");

        AssertSavePendingGate(saveBody);
        AssertExportPendingGate(exportBody);
    }

    private static string GetTerrainManagerSource()
    {
        return File.ReadAllText(GetTerrainManagerSourcePath());
    }

    private static string GetEditorShellViewModelSource()
    {
        return File.ReadAllText(GetEditorShellViewModelSourcePath());
    }

    private static string GetLoadFromResourceSessionBody()
    {
        return GetBlockAfter(GetTerrainManagerSource(), "public async Task<List<EditorTerrainEntity>> LoadFromResourceSession(EditorResourceSession session)");
    }

    private static string GetEditorShellMethodBody(string marker)
    {
        return GetBlockAfter(GetEditorShellViewModelSource(), marker);
    }

    private static string GetBlockAfter(string source, string marker)
    {
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

    private static void AssertSavePendingGate(string methodBody)
    {
        int pendingGate = methodBody.IndexOf("if (_resourceSession.HasPendingHeightmap)", StringComparison.Ordinal);
        int warning = methodBody.IndexOf("AddConsole(\"Warning\", \"Terrain workspace is waiting for a heightmap before save/export.\");", StringComparison.Ordinal);
        int returnAfterWarning = methodBody.IndexOf("return;", warning, StringComparison.Ordinal);
        int saveCall = methodBody.IndexOf("terrainManager.SaveAuthoringResources(session, snapshot, progress)", StringComparison.Ordinal);

        TestHarness.Assert(pendingGate >= 0, "save should branch on pending heightmap");
        TestHarness.Assert(warning >= 0, "save should log explicit pending warning");
        TestHarness.Assert(returnAfterWarning >= 0, "save pending warning should return immediately");
        TestHarness.Assert(saveCall >= 0, "save should still call SaveAuthoringResources with the captured snapshot on the non-pending path");
        TestHarness.Assert(pendingGate < warning, "save should log warning inside the pending gate");
        TestHarness.Assert(warning < returnAfterWarning, "save should return after pending warning");
        TestHarness.Assert(pendingGate < saveCall, "save pending gate should happen before SaveAuthoringResources");
    }

    private static void AssertExportPendingGate(string methodBody)
    {
        int pendingGate = methodBody.IndexOf("if (_resourceSession.HasPendingHeightmap)", StringComparison.Ordinal);
        int warning = methodBody.IndexOf("AddConsole(\"Warning\", \"Terrain workspace is waiting for a heightmap before save/export.\");", StringComparison.Ordinal);
        int returnAfterWarning = methodBody.IndexOf("return;", warning, StringComparison.Ordinal);
        int exporterAssign = methodBody.IndexOf("_terrainExporter.TerrainManager = terrainManager;", StringComparison.Ordinal);
        int executeAsync = methodBody.IndexOf("ExecuteAsync(", StringComparison.Ordinal);

        TestHarness.Assert(pendingGate >= 0, "export should branch on pending heightmap");
        TestHarness.Assert(warning >= 0, "export should log explicit pending warning");
        TestHarness.Assert(returnAfterWarning >= 0, "export pending warning should return immediately");
        TestHarness.Assert(exporterAssign >= 0, "export should still assign terrain manager on the non-pending path");
        TestHarness.Assert(executeAsync >= 0, "export should still execute the exporter on the non-pending path");
        TestHarness.Assert(pendingGate < warning, "export should log warning inside the pending gate");
        TestHarness.Assert(warning < returnAfterWarning, "export should return after pending warning");
        TestHarness.Assert(pendingGate < exporterAssign, "export pending gate should happen before terrain exporter assignment");
        TestHarness.Assert(pendingGate < executeAsync, "export pending gate should happen before ExecuteAsync");
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

    private static string GetEditorShellViewModelSourcePath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "Terrain.Editor",
            "ViewModels",
            "EditorShellViewModel.cs"));
    }
}
