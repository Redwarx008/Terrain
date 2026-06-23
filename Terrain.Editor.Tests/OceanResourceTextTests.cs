using Terrain.Rendering.Ocean;

namespace Terrain.Editor.Tests;

internal static class OceanResourceTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("ocean resource loader declares required water files", RequiredFileNamesIncludeSharedWaterFiles);
        TestHarness.Run("ocean resource loader disposes textures before reload", OceanResourceLoaderDisposesTexturesBeforeReload);
        TestHarness.Run("ocean flowmap exists in local game resources", FlowmapExistsInLocalGameResources);
        TestHarness.Run("river resource loader keeps shared flowmap lifecycle", RiverResourceLoaderKeepsSharedFlowmapLifecycle);
        TestHarness.Run("ocean water textures are not bundle roots", OceanWaterTexturesAreNotBundleRoots);
    }

    private static void RequiredFileNamesIncludeSharedWaterFiles()
    {
        string[] expected =
        [
            "water_color.dds",
            "ambient_normal.dds",
            "flowmap.dds",
            "flow_normal.dds",
            "foam.dds",
            "foam_ramp.dds",
            "foam_map.dds",
            "foam_noise.dds",
        ];

        foreach (string fileName in expected)
            TestHarness.Assert(OceanResourceLoader.RequiredFileNames.Contains(fileName), $"OceanResourceLoader.RequiredFileNames should include {fileName}");

        TestHarness.AssertEqual(expected.Length, OceanResourceLoader.RequiredFileNames.Count, "OceanResourceLoader.RequiredFileNames should only list the shared water files");
    }

    private static void OceanResourceLoaderDisposesTexturesBeforeReload()
    {
        string loader = ReadRepositoryText("Terrain/Rendering/Ocean/OceanResourceLoader.cs");

        int loadStart = loader.IndexOf("public void Load(GraphicsDevice graphicsDevice)", StringComparison.Ordinal);
        int disposeCall = loader.IndexOf("Dispose();", loadStart, StringComparison.Ordinal);
        int firstTextureLoad = loader.IndexOf("WaterColor = LoadRequiredLocalTexture", loadStart, StringComparison.Ordinal);

        TestHarness.Assert(loadStart >= 0, "OceanResourceLoader should expose Load(GraphicsDevice)");
        TestHarness.Assert(disposeCall >= 0, "OceanResourceLoader.Load should dispose previous textures before reload");
        TestHarness.Assert(firstTextureLoad >= 0, "OceanResourceLoader.Load should load water textures");
        TestHarness.Assert(disposeCall < firstTextureLoad, "OceanResourceLoader.Load should dispose previous textures before loading replacements");
    }

    private static void FlowmapExistsInLocalGameResources()
    {
        string fullPath = GetRepositoryPath("game/map/water/flowmap.dds");

        TestHarness.Assert(File.Exists(fullPath), "flowmap.dds should exist in game/map/water for direct shared water loading");
        TestHarness.Assert(new FileInfo(fullPath).Length > 0, "flowmap.dds should not be empty");
    }

    private static void RiverResourceLoaderKeepsSharedFlowmapLifecycle()
    {
        string loader = ReadRepositoryText("Terrain/Rendering/River/RiverResourceLoader.cs");

        AssertContains(loader, "FlowMapFileName = \"flowmap.dds\"", "RiverResourceLoader should declare the shared flowmap file");
        AssertContains(loader, "Texture? FlowMap", "RiverResourceLoader should expose the shared flowmap texture");
        AssertContains(loader, "FlowMap = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FlowMapFileName, loadAsSrgb: false);", "RiverResourceLoader should load flowmap as a linear DDS");
        AssertContains(loader, "DisposeLocalTexture(FlowMap);", "RiverResourceLoader should dispose flowmap during unload");
        AssertContains(loader, "FlowMap = null;", "RiverResourceLoader should clear flowmap during dispose");
    }

    private static void OceanWaterTexturesAreNotBundleRoots()
    {
        string editorPackage = ReadRepositoryText("Terrain.Editor/Terrain.Editor.sdpkg");
        string terrainPackage = ReadRepositoryText("Terrain/Terrain.sdpkg");

        foreach (string fileName in OceanResourceLoader.RequiredFileNames)
        {
            string assetName = Path.GetFileNameWithoutExtension(fileName).Replace('_', '-');
            AssertNotContains(editorPackage, $":Ocean/Water/{assetName}", $"{fileName} should not be an editor RootAsset");
            AssertNotContains(terrainPackage, $":Ocean/Water/{assetName}", $"{fileName} should not be a runtime RootAsset");
            AssertNotContains(editorPackage, $":River/Water/{assetName}", $"{fileName} should not be an editor River/Water RootAsset");
            AssertNotContains(terrainPackage, $":River/Water/{assetName}", $"{fileName} should not be a runtime River/Water RootAsset");
        }
    }

    private static string ReadRepositoryText(string relativePath)
    {
        return File.ReadAllText(GetRepositoryPath(relativePath));
    }

    private static string GetRepositoryPath(string relativePath)
    {
        return Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepositoryRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Terrain.sln")))
                return current;

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
                break;
            current = parent ?? string.Empty;
        }

        throw new InvalidOperationException("Unable to locate repository root from test base directory.");
    }

    private static void AssertContains(string text, string expected, string message)
    {
        TestHarness.Assert(text.Contains(expected, StringComparison.Ordinal), message);
    }

    private static void AssertNotContains(string text, string unexpected, string message)
    {
        TestHarness.Assert(!text.Contains(unexpected, StringComparison.Ordinal), message);
    }
}
