using System.Reflection;
using Stride.Graphics;
using Terrain.Editor.Rendering.River;

namespace Terrain.Editor.Tests;

internal static class RiverRenderFeatureRuntimeTests
{
    public static void RunAll()
    {
        TestHarness.Run("river dual-source blend state keeps payload alpha direct-write", DualSourceBlendStateKeepsPayloadAlphaDirectWrite);
        TestHarness.Run("river surface blend state matches target non-premultiplied rgb-only pass", SurfaceBlendStateMatchesTargetNonPremultipliedRgbOnlyPass);
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

    private static void SurfaceBlendStateMatchesTargetNonPremultipliedRgbOnlyPass()
    {
        MethodInfo? createBlendState = typeof(RiverRenderFeature).GetMethod("CreateSurfaceBlendState", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(createBlendState != null, "RiverRenderFeature should keep a dedicated surface blend-state factory");

        object? result = createBlendState!.Invoke(null, null);
        TestHarness.Assert(result is BlendStateDescription, "RiverRenderFeature surface blend-state factory should return a BlendStateDescription");

        var blendState = (BlendStateDescription)result!;
        TestHarness.Assert(blendState.RenderTargets[0].BlendEnable, "Surface RT0 should keep color blending enabled");
        TestHarness.AssertEqual(Blend.SourceAlpha, blendState.RenderTargets[0].ColorSourceBlend, "Surface RT0 color should use non-premultiplied source alpha like the target pass");
        TestHarness.AssertEqual(Blend.InverseSourceAlpha, blendState.RenderTargets[0].ColorDestinationBlend, "Surface RT0 color should preserve destination by inverse source alpha");
        TestHarness.AssertEqual(ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue, blendState.RenderTargets[0].ColorWriteChannels, "Surface RT0 should only write RGB like the target pass");
    }
}
