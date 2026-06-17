using System.Reflection;
using Stride.Graphics;
using Terrain.Editor.Rendering.River;

namespace Terrain.Editor.Tests;

internal static class RiverRenderFeatureRuntimeTests
{
    public static void RunAll()
    {
        TestHarness.Run("river dual-source blend state keeps payload alpha direct-write", DualSourceBlendStateKeepsPayloadAlphaDirectWrite);
    }

    private static void DualSourceBlendStateKeepsPayloadAlphaDirectWrite()
    {
        MethodInfo? createBlendState = typeof(RiverRenderFeature).GetMethod("CreateDualSourceBlendState", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(createBlendState != null, "RiverRenderFeature should keep a dedicated dual-source blend-state factory");

        object? result = createBlendState!.Invoke(null, null);
        TestHarness.Assert(result is BlendStateDescription, "RiverRenderFeature blend-state factory should return a BlendStateDescription");

        var blendState = (BlendStateDescription)result!;
        TestHarness.Assert(blendState.RenderTargets[0].BlendEnable, "Bottom RT0 should keep color blending enabled");
        TestHarness.AssertEqual(Blend.SecondarySourceAlpha, blendState.RenderTargets[0].ColorSourceBlend, "Bottom RT0 color should still blend by coverage");
        TestHarness.AssertEqual(Blend.InverseSecondarySourceAlpha, blendState.RenderTargets[0].ColorDestinationBlend, "Bottom RT0 color should still preserve scene seed outside coverage");
        TestHarness.AssertEqual(Blend.One, blendState.RenderTargets[0].AlphaSourceBlend, "Bottom RT0 alpha should write payload directly");
        TestHarness.AssertEqual(Blend.Zero, blendState.RenderTargets[0].AlphaDestinationBlend, "Bottom RT0 alpha should ignore scene-seed alpha");
    }
}
