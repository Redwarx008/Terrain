using System.Reflection;
using Stride.Core.Mathematics;
using Stride.Rendering;
using Terrain.Editor.Rendering;

namespace Terrain.Editor.Tests;

internal static class EditorTerrainShadowCasterTests
{
    public static void RunAll()
    {
        TestHarness.Run("editor terrain component defaults to casting shadows", EditorTerrainComponentDefaultsToCastingShadows);
        TestHarness.Run("editor terrain render state applies cast-shadows true", EditorTerrainRenderStateAppliesCastShadowsTrue);
        TestHarness.Run("editor terrain render state applies cast-shadows false", EditorTerrainRenderStateAppliesCastShadowsFalse);
    }

    private static void EditorTerrainComponentDefaultsToCastingShadows()
    {
        var property = typeof(EditorTerrainComponent).GetProperty("CastShadows", BindingFlags.Instance | BindingFlags.Public);
        TestHarness.Assert(property != null, "EditorTerrainComponent should expose a public CastShadows property, matching runtime terrain shadow behavior");

        var component = new EditorTerrainComponent();
        var value = property!.GetValue(component);
        TestHarness.AssertEqual(true, value, "EditorTerrainComponent.CastShadows should default to true so editor terrain can participate in shadow-map rendering");
    }

    private static void EditorTerrainRenderStateAppliesCastShadowsTrue()
    {
        VerifyRenderStateApplication(componentEnabled: true, castShadows: true, expectedShadowCaster: true);
    }

    private static void EditorTerrainRenderStateAppliesCastShadowsFalse()
    {
        VerifyRenderStateApplication(componentEnabled: false, castShadows: false, expectedShadowCaster: false);
    }

    private static void VerifyRenderStateApplication(bool componentEnabled, bool castShadows, bool expectedShadowCaster)
    {
        MethodInfo? applyRenderObjectState = typeof(EditorTerrainProcessor).GetMethod("ApplyRenderObjectState", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(applyRenderObjectState != null, "EditorTerrainProcessor should expose a dedicated render-state application helper so shadow-caster behavior can be regression-tested");

        var component = new EditorTerrainComponent
        {
            Enabled = componentEnabled,
            CastShadows = castShadows,
        };
        var renderObject = new EditorTerrainRenderObject();
        var worldOffset = new Vector3(11.0f, 22.0f, 33.0f);
        var bounds = new BoundingBox(new Vector3(1.0f, 2.0f, 3.0f), new Vector3(4.0f, 5.0f, 6.0f));

        applyRenderObjectState!.Invoke(null, [component, renderObject, worldOffset, bounds]);

        TestHarness.AssertEqual(componentEnabled, renderObject.Enabled, "Render-state application should mirror EditorTerrainComponent.Enabled");
        TestHarness.AssertEqual(RenderGroup.Group0, renderObject.RenderGroup, "Editor terrain should stay in Group0 after render-state application");
        TestHarness.AssertEqual(worldOffset, renderObject.World.TranslationVector, "Render-state application should translate the terrain to the entity world offset");
        TestHarness.AssertEqual((BoundingBoxExt)bounds, renderObject.BoundingBox, "Render-state application should publish the terrain bounds to the render mesh");
        TestHarness.AssertEqual(false, renderObject.IsScalingNegative, "Editor terrain render-state application should keep non-negative scaling");
        TestHarness.AssertEqual(expectedShadowCaster, renderObject.IsShadowCaster, "Render-state application should forward EditorTerrainComponent.CastShadows to RenderMesh.IsShadowCaster");
    }
}
