using System.Reflection;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Tests;

internal static class EditorTerrainShadowCasterTests
{
    public static void RunAll()
    {
        TestHarness.Run("editor terrain component defaults to casting shadows", EditorTerrainComponentDefaultsToCastingShadows);
        TestHarness.Run("editor terrain processor forwards cast-shadows flag to render mesh", EditorTerrainProcessorForwardsCastShadowsFlag);
    }

    private static void EditorTerrainComponentDefaultsToCastingShadows()
    {
        var property = typeof(EditorTerrainComponent).GetProperty("CastShadows", BindingFlags.Instance | BindingFlags.Public);
        TestHarness.Assert(property != null, "EditorTerrainComponent should expose a public CastShadows property, matching runtime terrain shadow behavior");

        var component = new EditorTerrainComponent();
        var value = property!.GetValue(component);
        TestHarness.AssertEqual(true, value, "EditorTerrainComponent.CastShadows should default to true so editor terrain can participate in shadow-map rendering");
    }

    private static void EditorTerrainProcessorForwardsCastShadowsFlag()
    {
        string source = File.ReadAllText(Path.Combine("Terrain.Editor", "Rendering", "EditorTerrainProcessor.cs"));

        TestHarness.Assert(source.Contains("renderObject.IsShadowCaster = component.CastShadows;", StringComparison.Ordinal),
            "EditorTerrainProcessor should forward EditorTerrainComponent.CastShadows to RenderMesh.IsShadowCaster");
        TestHarness.Assert(!source.Contains("renderObject.IsShadowCaster = false;", StringComparison.Ordinal),
            "EditorTerrainProcessor should not hard-disable terrain shadow casting");
    }
}
