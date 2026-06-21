using System.Xml.Linq;

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
        var document = XDocument.Parse(project);
        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include));

        TestHarness.Assert(
            projectReferences.All(include => !include!.Contains("Terrain.Editor", StringComparison.Ordinal)),
            "Terrain.Windows must not reference Terrain.Editor.");
    }

    private static void RuntimeSceneContainsRiverSystemComponent()
    {
        string scene = Read("Terrain", "Assets", "MainScene.sdscene");
        string? riverSystem = FindBlockContaining(scene, "-   Entity:", "Name: RiverSystem");

        TestHarness.Assert(
            riverSystem != null,
            "MainScene.sdscene should define a RiverSystem entity.");
        TestHarness.Assert(
            riverSystem!.Contains("!Terrain.Rendering.River.RiverComponent,Terrain", StringComparison.Ordinal) ||
            riverSystem.Contains("!RiverComponent", StringComparison.Ordinal),
            "RiverSystem should contain Terrain.Rendering.River.RiverComponent.");
    }

    private static void RuntimeCompositorRegistersRiverRenderFeature()
    {
        string compositor = Read("Terrain", "Assets", "GraphicsCompositor.sdgfxcomp");
        string? transparentStage = FindBlockContaining(compositor, ":", "Name: Transparent", requiredParentMarker: "RenderStages:");
        TestHarness.Assert(
            transparentStage != null,
            "GraphicsCompositor should keep a Transparent render stage for river surface rendering.");

        string transparentStageId = ReadField(transparentStage!, "Id:");
        string? riverFeature = FindBlockContaining(compositor, ":", "!Terrain.Rendering.River.RiverRenderFeature,Terrain", requiredParentMarker: "RenderFeatures:");

        TestHarness.Assert(
            riverFeature != null,
            "GraphicsCompositor.sdgfxcomp should register RiverRenderFeature from Terrain.");
        string? riverSurfaceSelector = FindBlockContaining(riverFeature!, ":", "EffectName: RiverSurface", requiredParentMarker: "RenderStageSelectors:");
        TestHarness.Assert(
            riverSurfaceSelector != null,
            "RiverRenderFeature selector should target RiverSurface.");
        TestHarness.Assert(
            riverSurfaceSelector!.Contains("RenderGroup: Group1", StringComparison.Ordinal),
            "RiverRenderFeature selector should use Group1.");
        TestHarness.Assert(
            riverSurfaceSelector.Contains($"RenderStage: ref!! {transparentStageId}", StringComparison.Ordinal) ||
            riverSurfaceSelector.Contains($"TransparentRenderStage: ref!! {transparentStageId}", StringComparison.Ordinal),
            "RiverRenderFeature selector should reference the Transparent render stage.");
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

    private static string? FindBlockContaining(string text, string startMarker, string containedText, string? requiredParentMarker = null)
    {
        string[] lines = NormalizeLineEndings(text).Split('\n');
        int parentStart = 0;
        int parentEnd = lines.Length;
        if (requiredParentMarker != null)
        {
            int parentIndex = FindLine(lines, 0, lines.Length, line => line.TrimStart().StartsWith(requiredParentMarker, StringComparison.Ordinal));
            if (parentIndex < 0)
                return null;

            parentStart = parentIndex + 1;
            parentEnd = FindBlockEnd(lines, parentIndex, IndentOf(lines[parentIndex]));
        }

        for (int i = parentStart; i < parentEnd; i++)
        {
            if (!IsBlockStart(lines[i], startMarker))
                continue;

            int end = FindBlockEnd(lines, i, IndentOf(lines[i]));
            string block = JoinLines(lines, i, end);
            if (block.Contains(containedText, StringComparison.Ordinal))
                return block;
        }

        return null;
    }

    private static bool IsBlockStart(string line, string marker)
    {
        string trimmed = line.TrimStart();
        return marker == ":"
            ? trimmed.Contains(": ", StringComparison.Ordinal) || trimmed.EndsWith(':')
            : trimmed.StartsWith(marker, StringComparison.Ordinal);
    }

    private static int FindBlockEnd(string[] lines, int start, int startIndent)
    {
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            if (IndentOf(lines[i]) <= startIndent)
                return i;
        }

        return lines.Length;
    }

    private static int FindLine(string[] lines, int start, int end, Func<string, bool> predicate)
    {
        for (int i = start; i < end; i++)
        {
            if (predicate(lines[i]))
                return i;
        }

        return -1;
    }

    private static string ReadField(string block, string fieldName)
    {
        string? line = NormalizeLineEndings(block)
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith(fieldName, StringComparison.Ordinal));

        TestHarness.Assert(line != null, $"Expected field {fieldName} in block.");
        return line![(fieldName.Length)..].Trim();
    }

    private static int IndentOf(string line)
    {
        int indent = 0;
        while (indent < line.Length && line[indent] == ' ')
            indent++;

        return indent;
    }

    private static string JoinLines(string[] lines, int start, int end)
    {
        return string.Join('\n', lines.Skip(start).Take(end - start));
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
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
