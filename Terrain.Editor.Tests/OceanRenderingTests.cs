#nullable enable

using System.Reflection;
using Stride.Core.Mathematics;
using Stride.Engine.Design;
using Stride.Graphics;
using Terrain.Rendering.Ocean;

namespace Terrain.Editor.Tests;

internal static class OceanRenderingTests
{
    public static void RunAll()
    {
        TestHarness.Run("ocean component uses ocean processor renderer", OceanComponentUsesOceanProcessorRenderer);
        TestHarness.Run("ocean component does not expose sea level property", OceanComponentDoesNotExposeSeaLevelProperty);
        TestHarness.Run("ocean component clears runtime input", OceanComponentClearsRuntimeInput);
        TestHarness.Run("ocean vertex layout exposes position and uv", OceanVertexLayoutExposesPositionAndUv);
        TestHarness.Run("map surface applies ocean runtime input", MapSurfaceAppliesOceanRuntimeInput);
        TestHarness.Run("map surface clears ocean runtime input when context unavailable", MapSurfaceClearsOceanRuntimeInputWhenContextUnavailable);
        TestHarness.Run("ocean render object builds full map quad data", OceanRenderObjectBuildsFullMapQuadData);
        TestHarness.Run("ocean water pass uses strict depth read", OceanWaterPassUsesStrictDepthRead);
        TestHarness.Run("ocean water pass preserves render target alpha", OceanWaterPassPreservesRenderTargetAlpha);
    }

    private static void OceanComponentUsesOceanProcessorRenderer()
    {
        var attribute = typeof(OceanComponent)
            .GetCustomAttributes()
            .OfType<DefaultEntityComponentRendererAttribute>()
            .FirstOrDefault();

        TestHarness.Assert(attribute != null, "OceanComponent should declare a render processor");
        TestHarness.AssertEqual(typeof(OceanProcessor).AssemblyQualifiedName, attribute!.TypeName, "renderer type");
    }

    private static void OceanComponentDoesNotExposeSeaLevelProperty()
    {
        PropertyInfo? seaLevel = typeof(OceanComponent).GetProperty(
            "SeaLevel",
            BindingFlags.Instance | BindingFlags.Public);

        TestHarness.Assert(seaLevel == null, "SeaLevel should stay in map settings/runtime input, not OceanComponent public API");
    }

    private static void OceanComponentClearsRuntimeInput()
    {
        var component = new OceanComponent();

        component.ApplyRuntimeInput(new OceanRuntimeInput(7.5f, new Vector2(128.0f, 64.0f)));
        component.ClearRuntimeInput();

        TestHarness.Assert(component.RuntimeInput == null, "Ocean runtime input should be null after ClearRuntimeInput");
    }

    private static void OceanVertexLayoutExposesPositionAndUv()
    {
        var elements = OceanVertex.Layout.VertexElements;

        TestHarness.AssertEqual(2, elements.Length, "OceanVertex layout element count");
        TestHarness.AssertEqual("POSITION", elements[0].SemanticAsText, "OceanVertex position semantic");
        TestHarness.AssertEqual("TEXCOORD", elements[1].SemanticAsText, "OceanVertex uv semantic");
        TestHarness.AssertEqual(System.Runtime.InteropServices.Marshal.SizeOf<OceanVertex>(), OceanVertex.Layout.VertexStride, "OceanVertex layout stride");
    }

    private static void MapSurfaceAppliesOceanRuntimeInput()
    {
        string source = ReadRepositoryText("Terrain/MapSurface/MapSurfaceProcessor.cs");

        TestHarness.Assert(source.Contains("Get<OceanComponent>()", StringComparison.Ordinal), "MapSurfaceProcessor should look up OceanComponent from OceanEntity");
        TestHarness.Assert(source.Contains("ApplyRuntimeInput(new OceanRuntimeInput(resources.SeaLevel, mapWorldSize))", StringComparison.Ordinal), "MapSurfaceProcessor should apply sea level and map size to OceanRuntimeInput");
    }

    private static void MapSurfaceClearsOceanRuntimeInputWhenContextUnavailable()
    {
        string source = ReadRepositoryText("Terrain/MapSurface/MapSurfaceProcessor.cs");

        TestHarness.Assert(source.Contains("ClearRuntimeContext(state);", StringComparison.Ordinal), "MapSurfaceProcessor should clear stale terrain context when unavailable");
        TestHarness.Assert(source.Contains("ClearOceanRuntimeInputIfPresent(component);", StringComparison.Ordinal), "MapSurfaceProcessor should clear stale ocean input when terrain context is unavailable");
        TestHarness.Assert(source.Contains("component.OceanEntity?.Get<OceanComponent>()?.ClearRuntimeInput();", StringComparison.Ordinal), "MapSurfaceProcessor should clear ocean input without warning when OceanEntity is missing");
    }

    private static void OceanRenderObjectBuildsFullMapQuadData()
    {
        var input = new OceanRuntimeInput(7.5f, new Vector2(128.0f, 64.0f));
        var quad = OceanRenderObject.BuildQuad(input);
        using var renderObject = new OceanRenderObject();

        renderObject.ApplyCpuQuadState(input, quad);

        TestHarness.AssertEqual(4, quad.Vertices.Length, "Ocean quad vertex count");
        TestHarness.AssertEqual(6, quad.Indices.Length, "Ocean quad index count");
        TestHarness.Assert(quad.Indices.SequenceEqual(new[] { 0, 1, 2, 0, 2, 3 }), "Ocean quad index order should stay stable");
        TestHarness.AssertEqual(new Vector4(0.0f, 7.5f, 0.0f, 1.0f), quad.Vertices[0].Position, "Ocean quad first corner");
        TestHarness.AssertEqual(new Vector4(128.0f, 7.5f, 0.0f, 1.0f), quad.Vertices[1].Position, "Ocean quad second corner");
        TestHarness.AssertEqual(new Vector4(128.0f, 7.5f, 64.0f, 1.0f), quad.Vertices[2].Position, "Ocean quad opposite corner");
        TestHarness.AssertEqual(new Vector4(0.0f, 7.5f, 64.0f, 1.0f), quad.Vertices[3].Position, "Ocean quad fourth corner");
        TestHarness.AssertEqual(new Vector2(1.0f, 1.0f), quad.Vertices[2].UV, "Ocean quad uv opposite corner");
        Vector3 edgeA = quad.Vertices[1].Position.XYZ() - quad.Vertices[0].Position.XYZ();
        Vector3 edgeB = quad.Vertices[2].Position.XYZ() - quad.Vertices[0].Position.XYZ();
        TestHarness.Assert(Vector3.Cross(edgeA, edgeB).Y < 0.0f, "Ocean quad first triangle winding should stay stable");
        TestHarness.AssertEqual(7.5f - OceanRenderObject.BoundsVerticalPadding, quad.BoundingBox.Minimum.Y, "Ocean bounds minimum y padding");
        TestHarness.AssertEqual(7.5f + OceanRenderObject.BoundsVerticalPadding, quad.BoundingBox.Maximum.Y, "Ocean bounds maximum y padding");
        TestHarness.Assert(quad.BoundingBox.Maximum.Y > quad.BoundingBox.Minimum.Y, "Ocean bounds should have non-zero Y thickness");
        TestHarness.AssertEqual(7.5f, renderObject.SeaLevel, "OceanRenderObject stores sea level");
        TestHarness.AssertEqual(new Vector2(128.0f, 64.0f), renderObject.MapWorldSize, "OceanRenderObject stores map size");
        TestHarness.AssertEqual(6, renderObject.IndexCount, "OceanRenderObject stores index count");
        TestHarness.Assert(renderObject.Matches(input), "OceanRenderObject should match the input used to build it");
    }

    private static void OceanWaterPassUsesStrictDepthRead()
    {
        MethodInfo? createDepthStencilState = typeof(OceanRenderFeature).GetMethod("CreateDepthStencilState", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(createDepthStencilState != null, "OceanRenderFeature should keep a dedicated depth-stencil-state factory");

        object? result = createDepthStencilState!.Invoke(null, null);
        TestHarness.Assert(result is DepthStencilStateDescription, "Ocean depth-state factory should return a DepthStencilStateDescription");

        var state = (DepthStencilStateDescription)result!;
        TestHarness.Assert(state.DepthBufferEnable, "Ocean pass should keep depth testing enabled");
        TestHarness.Assert(!state.DepthBufferWriteEnable, "Ocean pass should not write scene depth");
        TestHarness.AssertEqual(CompareFunction.Less, state.DepthBufferFunction, "Ocean pass should reject equal-depth fragments like the validated water path");
    }

    private static void OceanWaterPassPreservesRenderTargetAlpha()
    {
        MethodInfo? createBlendState = typeof(OceanRenderFeature).GetMethod("CreateBlendState", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(createBlendState != null, "OceanRenderFeature should keep a dedicated blend-state factory");

        object? result = createBlendState!.Invoke(null, null);
        TestHarness.Assert(result is BlendStateDescription, "Ocean blend-state factory should return a BlendStateDescription");

        var blendState = (BlendStateDescription)result!;
        TestHarness.Assert(blendState.RenderTargets[0].BlendEnable, "Ocean pass should keep color blending enabled");
        TestHarness.AssertEqual(Blend.SourceAlpha, blendState.RenderTargets[0].ColorSourceBlend, "Ocean pass should use non-premultiplied source alpha");
        TestHarness.AssertEqual(Blend.InverseSourceAlpha, blendState.RenderTargets[0].ColorDestinationBlend, "Ocean pass should preserve destination RGB by inverse source alpha");
        TestHarness.AssertEqual(ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue, blendState.RenderTargets[0].ColorWriteChannels, "Ocean pass should not overwrite scene alpha");
    }

    private static string ReadRepositoryText(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            string gitPath = Path.Combine(directory, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
