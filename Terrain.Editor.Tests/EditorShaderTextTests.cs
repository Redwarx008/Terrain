namespace Terrain.Editor.Tests;

internal static class EditorShaderTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("editor project registers editor shader key files", EditorProjectRegistersShaderKeyFiles);
    }

    private static void EditorProjectRegistersShaderKeyFiles()
    {
        string project = ReadRepositoryText("Terrain.Editor/Terrain.Editor.csproj");
        string package = ReadRepositoryText("Terrain.Editor/Terrain.Editor.sdpkg");

        AssertContains(package, "!dir Effects", "Terrain.Editor.sdpkg should include the Effects folder that contains editor shaders");
        AssertContains(project, "<Compile Update=\"Effects\\BrushDecalShader.sdsl.cs\">", "Terrain.Editor.csproj should compile generated BrushDecalShader keys");
        AssertContains(project, "<Compile Update=\"Effects\\EditorTerrainBuildSplatMap.sdsl.cs\">", "Terrain.Editor.csproj should compile generated EditorTerrainBuildSplatMap keys");
        AssertContains(project, "<Compile Update=\"Effects\\EditorTerrainDiffuse.sdsl.cs\">", "Terrain.Editor.csproj should compile generated EditorTerrainDiffuse keys");
        AssertContains(project, "<Compile Update=\"Effects\\EditorTerrainDisplacement.sdsl.cs\">", "Terrain.Editor.csproj should compile generated EditorTerrainDisplacement keys");
        AssertContains(project, "<Compile Update=\"Effects\\EditorTerrainForwardShadingEffect.sdfx.cs\">", "Terrain.Editor.csproj should compile generated EditorTerrainForwardShadingEffect keys");
        AssertContains(project, "<Compile Update=\"Effects\\EditorTerrainHeightParameters.sdsl.cs\">", "Terrain.Editor.csproj should compile generated EditorTerrainHeightParameters keys");
        AssertContains(project, "<Compile Update=\"Effects\\EditorTerrainHeightStream.sdsl.cs\">", "Terrain.Editor.csproj should compile generated EditorTerrainHeightStream keys");
        AssertContains(project, "<None Update=\"Effects\\**\\*.sdsl\" Generator=\"StrideShaderKeyGenerator\" />", "Terrain.Editor.csproj should keep editor SDSL files registered for Stride shader key generation");
        AssertContains(project, "<None Update=\"Effects\\**\\*.sdfx\" Generator=\"StrideShaderKeyGenerator\" />", "Terrain.Editor.csproj should keep editor SDFX files registered for Stride shader key generation");
        AssertNotContains(project, "<AdditionalFiles Include=\"Effects\\**\\*.sdsl\" />", "Terrain.Editor.csproj should not move editor shader sources out of the Stride None item pipeline");
        AssertNotContains(project, "<AdditionalFiles Include=\"Effects\\**\\*.sdfx\" />", "Terrain.Editor.csproj should not move editor effect sources out of the Stride None item pipeline");
    }

    private static string ReadRepositoryText(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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

        throw new InvalidOperationException("Could not locate repository root.");
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
