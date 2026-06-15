using Terrain.Editor.Services.Resources;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorMaterialLoadStateTests
{
    public static void RunAll()
    {
        TestHarness.Run("missing material ids block save and export", MissingMaterialIdsBlockSaveAndExport);
        TestHarness.Run("texture-only fallback keeps save and export enabled", TextureOnlyFallbackKeepsSaveAndExportEnabled);
    }

    private static void MissingMaterialIdsBlockSaveAndExport()
    {
        EditorResourceSession session = CreateSession();
        session.ApplyMaterialLoadState(new EditorMaterialLoadState(
            [
                new EditorMaterialLoadIssue(
                    EditorMaterialLoadIssueKind.MissingMaterialId,
                    "rock",
                    "Terrain material id 'rock' is referenced by biome settings but missing from descriptor: C:\\temp\\descriptor.toml",
                    "C:\\temp\\descriptor.toml"),
            ],
            hasBlockingMissingMaterialIds: true,
            hasTextureFallbacks: false));

        TestHarness.Assert(session.HasMaterialLoadIssues, "session should expose material issues");
        TestHarness.Assert(session.HasBlockingMaterialIssues, "missing material ids should be blocking");
        TestHarness.Assert(!session.CanSaveAuthoringResources, "blocking material issues should disable save");
        TestHarness.Assert(!session.CanExportTerrainData, "blocking material issues should disable export");
    }

    private static void TextureOnlyFallbackKeepsSaveAndExportEnabled()
    {
        EditorResourceSession session = CreateSession();
        session.ApplyMaterialLoadState(new EditorMaterialLoadState(
            [
                new EditorMaterialLoadIssue(
                    EditorMaterialLoadIssueKind.MissingAlbedoTexture,
                    "grass",
                    "Terrain material 'grass' is missing albedo texture. Falling back to magenta missing-material diffuse: C:\\temp\\grass.png",
                    "C:\\temp\\grass.png"),
            ],
            hasBlockingMissingMaterialIds: false,
            hasTextureFallbacks: true));

        TestHarness.Assert(session.HasMaterialLoadIssues, "session should expose texture fallback issues");
        TestHarness.Assert(!session.HasBlockingMaterialIssues, "texture fallback should not be blocking");
        TestHarness.Assert(session.CanSaveAuthoringResources, "texture fallback should keep save enabled");
        TestHarness.Assert(session.CanExportTerrainData, "texture fallback should keep export enabled");
    }

    private static EditorResourceSession CreateSession()
    {
        static ResolvedGameResource Resource(string virtualPath, string path)
        {
            return new ResolvedGameResource(virtualPath, path, "mod", IsWritable: true, HasLowerPriorityFallback: false);
        }

        return new EditorResourceSession(
            Resource("map_data/default.toml", "C:\\temp\\default.toml"),
            Resource("map_data/heightmap.png", "C:\\temp\\heightmap.png"),
            Resource("map_data/terrain.terrain", "C:\\temp\\terrain.terrain"),
            Resource("map_data/biome_mask.png", "C:\\temp\\biome_mask.png"),
            Resource("map_data/biome_settings.toml", "C:\\temp\\biome_settings.toml"),
            Resource("map_data/materials/descriptor.toml", "C:\\temp\\descriptor.toml"),
            new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 100.0f,
            });
    }
}
