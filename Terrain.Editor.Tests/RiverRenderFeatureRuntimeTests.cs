using System.Reflection;
using Stride.Graphics;
using Terrain.Editor.Rendering.River;

namespace Terrain.Editor.Tests;

internal static class RiverRenderFeatureRuntimeTests
{
    public static void RunAll()
    {
        TestHarness.Run("river dual-source blend state matches target color and alpha factors", DualSourceBlendStateMatchesTargetColorAndAlphaFactors);
        TestHarness.Run("river surface blend state matches target non-premultiplied rgb-only pass", SurfaceBlendStateMatchesTargetNonPremultipliedRgbOnlyPass);
        TestHarness.Run("river surface rasterizer uses stronger depth bias than bottom", SurfaceRasterizerUsesStrongerDepthBiasThanBottom);
        TestHarness.Run("river depth read uses strict less compare like target water", DepthReadUsesStrictLessCompareLikeTargetWater);
    }

    private static void DualSourceBlendStateMatchesTargetColorAndAlphaFactors()
    {
        MethodInfo? createBlendState = typeof(RiverRenderFeature).GetMethod("CreateDualSourceBlendState", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(createBlendState != null, "RiverRenderFeature should keep a dedicated dual-source blend-state factory");

        object? result = createBlendState!.Invoke(null, null);
        TestHarness.Assert(result is BlendStateDescription, "RiverRenderFeature blend-state factory should return a BlendStateDescription");

        var blendState = (BlendStateDescription)result!;
        TestHarness.Assert(blendState.RenderTargets[0].BlendEnable, "Bottom RT0 should keep color blending enabled");
        TestHarness.AssertEqual(Blend.SecondarySourceAlpha, blendState.RenderTargets[0].ColorSourceBlend, "Bottom RT0 color should still blend by coverage");
        TestHarness.AssertEqual(Blend.InverseSecondarySourceAlpha, blendState.RenderTargets[0].ColorDestinationBlend, "Bottom RT0 color should still preserve scene seed outside coverage");
        TestHarness.AssertEqual(Blend.SecondarySourceAlpha, blendState.RenderTargets[0].AlphaSourceBlend, "Bottom RT0 alpha should use the same dual-source coverage factor as the target bottom pass");
        TestHarness.AssertEqual(Blend.InverseSecondarySourceAlpha, blendState.RenderTargets[0].AlphaDestinationBlend, "Bottom RT0 alpha should preserve destination payload by inverse coverage like the target bottom pass");
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

    private static void SurfaceRasterizerUsesStrongerDepthBiasThanBottom()
    {
        MethodInfo? createRasterizerState = typeof(RiverRenderFeature).GetMethod("CreateRasterizerState", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(createRasterizerState != null, "RiverRenderFeature should keep a dedicated rasterizer-state factory");

        object? bottomResult = createRasterizerState!.Invoke(null, new object?[] { false, false });
        object? surfaceResult = createRasterizerState.Invoke(null, new object?[] { false, true });
        TestHarness.Assert(bottomResult is RasterizerStateDescription, "River bottom rasterizer factory should return a RasterizerStateDescription");
        TestHarness.Assert(surfaceResult is RasterizerStateDescription, "River surface rasterizer factory should return a RasterizerStateDescription");

        var bottomState = (RasterizerStateDescription)bottomResult!;
        var surfaceState = (RasterizerStateDescription)surfaceResult!;
        TestHarness.AssertEqual(-1, bottomState.DepthBias, "Bottom pass should keep its small historical depth bias");
        TestHarness.AssertEqual(CullMode.Back, surfaceState.CullMode, "Surface pass should cull back faces like the target water pass");
        TestHarness.AssertEqual(-50000, surfaceState.DepthBias, "Surface pass should use the target fixed bias with the editor camera near plane matched to the target");
        TestHarness.AssertEqual(0.0f, surfaceState.DepthBiasClamp, "Surface pass should not rely on clamp to compensate for an over-large fixed bias");
        TestHarness.AssertEqual(0.0f, surfaceState.SlopeScaleDepthBias, "Surface pass should not add slope bias on top of the CK3-scale fixed bias");

        MethodInfo? createRasterizerStateForRenderView = typeof(RiverRenderFeature).GetMethod("CreateRasterizerStateForRenderView", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(createRasterizerStateForRenderView != null, "RiverRenderFeature should adapt surface depth bias to the actual render-view near clip");

        object? legacyNearResult = createRasterizerStateForRenderView!.Invoke(null, new object?[] { false, true, 0.1f });
        object? targetNearResult = createRasterizerStateForRenderView.Invoke(null, new object?[] { false, true, 10.0f });
        TestHarness.Assert(legacyNearResult is RasterizerStateDescription, "Render-view rasterizer factory should return a RasterizerStateDescription for legacy near");
        TestHarness.Assert(targetNearResult is RasterizerStateDescription, "Render-view rasterizer factory should return a RasterizerStateDescription for target near");

        var legacyNearSurfaceState = (RasterizerStateDescription)legacyNearResult!;
        var targetNearSurfaceState = (RasterizerStateDescription)targetNearResult!;
        TestHarness.AssertEqual(-5000, legacyNearSurfaceState.DepthBias, "Legacy near=0.1 render views should derive a smaller surface bias from the target near-clip calibration");
        TestHarness.AssertEqual(-50000, targetNearSurfaceState.DepthBias, "Target near=10 render views should keep the CK3 raw surface bias");

        object? intermediateNearResult = createRasterizerStateForRenderView.Invoke(null, new object?[] { false, true, 2.5f });
        TestHarness.Assert(intermediateNearResult is RasterizerStateDescription, "Render-view rasterizer factory should return a RasterizerStateDescription for intermediate near");
        var intermediateNearSurfaceState = (RasterizerStateDescription)intermediateNearResult!;
        TestHarness.AssertEqual(-25000, intermediateNearSurfaceState.DepthBias, "Surface depth bias should scale continuously with the actual render-view near clip instead of switching between hard-coded presets");
    }

    private static void DepthReadUsesStrictLessCompareLikeTargetWater()
    {
        MethodInfo? createDepthStencilState = typeof(RiverRenderFeature).GetMethod("CreateDepthStencilState", BindingFlags.NonPublic | BindingFlags.Static);
        TestHarness.Assert(createDepthStencilState != null, "RiverRenderFeature should keep a dedicated depth-stencil-state factory");

        object? result = createDepthStencilState!.Invoke(null, null);
        TestHarness.Assert(result is DepthStencilStateDescription, "River depth-state factory should return a DepthStencilStateDescription");

        var state = (DepthStencilStateDescription)result!;
        TestHarness.Assert(state.DepthBufferEnable, "River passes should keep depth testing enabled");
        TestHarness.Assert(!state.DepthBufferWriteEnable, "River passes should not write scene depth");
        TestHarness.AssertEqual(CompareFunction.Less, state.DepthBufferFunction, "River passes should reject equal-depth hidden fragments instead of using LessEqual");
    }
}
