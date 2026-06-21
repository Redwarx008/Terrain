using System.Text.RegularExpressions;

namespace Terrain.Editor.Tests;

internal static class RuntimeRiverAssetTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("runtime project does not reference editor project", RuntimeProjectDoesNotReferenceEditorProject);
        TestHarness.Run("runtime scene contains river system component", RuntimeSceneContainsRiverSystemComponent);
        TestHarness.Run("runtime compositor registers river render feature", RuntimeCompositorRegistersRiverRenderFeature);
        TestHarness.Run("runtime river shaders live in terrain project", RuntimeRiverShadersLiveInTerrainProject);
        TestHarness.Run("terrain package roots river reflection cubemap", TerrainPackageRootsRiverReflectionCubemap);
    }

    private static void RuntimeProjectDoesNotReferenceEditorProject()
    {
        string project = Read("Terrain.Windows", "Terrain.Windows.csproj");

        TestHarness.Assert(
            !project.Contains("Terrain.Editor", StringComparison.Ordinal),
            "Terrain.Windows must not reference Terrain.Editor.");
    }

    private static void RuntimeSceneContainsRiverSystemComponent()
    {
        string scene = Read("Terrain", "Assets", "MainScene.sdscene");

        TestHarness.Assert(
            scene.Contains("Name: RiverSystem", StringComparison.Ordinal),
            "MainScene.sdscene should define a RiverSystem entity.");
        TestHarness.Assert(
            scene.Contains("!Terrain.Rendering.River.RiverComponent,Terrain", StringComparison.Ordinal) ||
            scene.Contains("!RiverComponent", StringComparison.Ordinal),
            "RiverSystem should contain Terrain.Rendering.River.RiverComponent.");
    }

    private static void RuntimeCompositorRegistersRiverRenderFeature()
    {
        string compositor = Read("Terrain", "Assets", "GraphicsCompositor.sdgfxcomp");

        TestHarness.Assert(
            compositor.Contains("!Terrain.Rendering.River.RiverRenderFeature,Terrain", StringComparison.Ordinal),
            "GraphicsCompositor.sdgfxcomp should register RiverRenderFeature from Terrain.");
        TestHarness.Assert(
            compositor.Contains("EffectName: RiverSurface", StringComparison.Ordinal),
            "RiverRenderFeature selector should target RiverSurface.");
        TestHarness.Assert(
            compositor.Contains("Name: Transparent", StringComparison.Ordinal),
            "GraphicsCompositor should keep a Transparent render stage for river surface rendering.");
        TestHarness.Assert(
            Regex.IsMatch(compositor, "RenderGroup:\\s*Group1"),
            "RiverRenderFeature selector should use Group1.");
    }

    private static void RuntimeRiverShadersLiveInTerrainProject()
    {
        string[] shaderNames =
        [
            "RiverBottom.sdsl",
            "RiverSurface.sdsl",
            "RiverSceneSeed.sdsl",
            "RiverCommon.sdsl",
            "RiverWaterCommon.sdsl",
            "RiverVertexStreams.sdsl",
            "RiverStrideLighting.sdsl",
        ];

        foreach (string shaderName in shaderNames)
        {
            TestHarness.Assert(
                File.Exists(Path.Combine(RepositoryRoot, "Terrain", "Effects", "River", shaderName)),
                $"{shaderName} should live under Terrain/Effects/River.");
            TestHarness.Assert(
                !File.Exists(Path.Combine(RepositoryRoot, "Terrain.Editor", "Effects", shaderName)),
                $"{shaderName} should not remain under Terrain.Editor/Effects.");
        }
    }

    private static void TerrainPackageRootsRiverReflectionCubemap()
    {
        string package = Read("Terrain", "Terrain.sdpkg");

        TestHarness.Assert(
            package.Contains("River/Environment/reflection-specular", StringComparison.Ordinal),
            "Terrain.sdpkg should root River/Environment/reflection-specular for runtime content loading.");
    }

    private static string Read(params string[] path)
    {
        return File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Terrain.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
