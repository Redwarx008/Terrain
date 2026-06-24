namespace Terrain.Editor.Tests;

internal static class WaterRenderingTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("custom forward renderer owns water draw order", CustomForwardRendererOwnsWaterDrawOrder);
        TestHarness.Run("water refraction capture pass uses canonical image shader", WaterRefractionCapturePassUsesCanonicalImageShader);
        TestHarness.Run("water refraction capture resources use half float target", WaterRefractionCaptureResourcesUseHalfFloatTarget);
        TestHarness.Run("water refraction capture shader packs river world space", WaterRefractionCaptureShaderPacksRiverWorldSpace);
        TestHarness.Run("water refraction capture uses current renderer depth", WaterRefractionCaptureUsesCurrentRendererDepth);
        TestHarness.Run("runtime compositor uses custom forward renderer", RuntimeCompositorUsesCustomForwardRenderer);
        TestHarness.Run("runtime compositor routes water stage through custom renderer", RuntimeCompositorRoutesWaterStageThroughCustomRenderer);
        TestHarness.Run("water rendering architecture avoids obsolete renderer names", WaterRenderingArchitectureAvoidsObsoleteRendererNames);
        TestHarness.Run("custom forward renderer rejects unsupported msaa outputs", CustomForwardRendererRejectsUnsupportedMsaaOutputs);
        TestHarness.Run("custom forward renderer includes ocean sea level in capture clamp", CustomForwardRendererIncludesOceanSeaLevelInCaptureClamp);
        TestHarness.Run("ocean render feature exposes renderer callable water draw", OceanRenderFeatureExposesRendererCallableWaterDraw);
        TestHarness.Run("river render feature exposes renderer callable water chain", RiverRenderFeatureExposesRendererCallableWaterChain);
    }

    private static void CustomForwardRendererOwnsWaterDrawOrder()
    {
        string rendererPath = GetRepositoryPath("Terrain/Rendering/CustomForwardRenderer.cs");
        TestHarness.Assert(File.Exists(rendererPath), "Terrain/Rendering/CustomForwardRenderer.cs should exist as the only explicit water-order renderer.");

        string renderer = File.ReadAllText(rendererPath);
        AssertContains(renderer, "public partial class CustomForwardRenderer : SceneRendererBase, ISharedRenderer", "CustomForwardRenderer should derive from SceneRendererBase and implement ISharedRenderer.");
        AssertContains(renderer, "DrawWaterRefractionCapture", "CustomForwardRenderer should call or expose DrawWaterRefractionCapture.");
        AssertContains(renderer, "DrawOceanWater", "CustomForwardRenderer should call or expose DrawOceanWater.");
        AssertContains(renderer, "DrawRiverWaterChain", "CustomForwardRenderer should call or expose DrawRiverWaterChain.");
        AssertContains(renderer, "public RenderStage? WaterRenderStage { get; set; }", "CustomForwardRenderer should expose the dedicated Water render stage.");
        AssertContains(renderer, "context.RenderView.RenderStages.Add(WaterRenderStage);", "CustomForwardRenderer should collect Water-stage render nodes through Stride culling/sorting.");
        AssertContains(renderer, "CollectOceanWaterRangesFromWaterStage", "CustomForwardRenderer should scan Ocean ranges from the Water render stage.");
        AssertContains(renderer, "CollectRiverWaterRangesFromWaterStage", "CustomForwardRenderer should scan River ranges from the Water render stage.");
        AssertContains(renderer, "riverRenderFeature.GetRiverMaxVisibleCameraHeight", "CustomForwardRenderer should resolve river visibility before issuing shared refraction capture.");
        AssertContains(renderer, "if (cameraWorldY >= riverMaxVisibleCameraHeight)", "CustomForwardRenderer should skip shared refraction capture when the camera is above every River range cutoff.");
        AssertContains(renderer, "if (oceanWaterRanges.Count == 0 && !hasVisibleRiverWater)", "CustomForwardRenderer should skip capture only when no Ocean exists and River is not visible.");
        AssertContains(renderer, "oceanRenderFeature.DrawWater(", "CustomForwardRenderer should invoke the renderer-callable Ocean draw path.");
        AssertContains(renderer, "Texture? sceneDepth = drawContext.CommandList.DepthStencilBuffer ?? currentDepthStencil;", "CustomForwardRenderer should pass the current renderer depth into shared capture.");
        AssertContains(renderer, "float refractionMaxCameraHeight = capture.RefractionMaxCameraHeight;", "CustomForwardRenderer should reuse the capture clamp for water readers.");
        AssertOccursBefore(
            renderer,
            "DrawOceanWater(context, drawContext, waterCapture);",
            "DrawRiverWaterChain(context, drawContext, waterCapture);",
            "CustomForwardRenderer should draw Ocean before River bottom/surface.");
        AssertOccursBefore(
            renderer,
            "riverRenderFeature.GetRiverMaxVisibleCameraHeight",
            "waterRefractionCapturePass.Capture(",
            "CustomForwardRenderer should perform River camera-height skip before shared refraction capture.");
        AssertOccursExactly(renderer, "waterRefractionCapturePass.Capture(", 1, "CustomForwardRenderer should create the shared water refraction capture once per water pass.");
        AssertNotContains(renderer, "BuildRiverWaterStageFromRenderObjects", "CustomForwardRenderer should not synthesize River draw ranges from raw RenderObjects.");
    }

    private static void WaterRefractionCapturePassUsesCanonicalImageShader()
    {
        string passPath = GetRepositoryPath("Terrain/Rendering/Water/WaterRefractionCapturePass.cs");
        TestHarness.Assert(File.Exists(passPath), "Terrain/Rendering/Water/WaterRefractionCapturePass.cs should exist.");

        string pass = File.ReadAllText(passPath);
        AssertContains(pass, "public sealed class WaterRefractionCapturePass", "WaterRefractionCapturePass should use the canonical helper name.");
        AssertContains(pass, "ImageEffectShader(\"WaterRefractionCapture", "WaterRefractionCapturePass should bind the WaterRefractionCapture image shader.");
        AssertContains(pass, "using (context.PushRenderTargetsAndRestore())", "WaterRefractionCapturePass should restore the active scene render target after capture.");
        AssertContains(pass, "Texture sceneDepthSource", "WaterRefractionCapturePass should receive an explicit scene depth source from the renderer.");
        AssertContains(pass, "context.Resolver.ResolveDepthStencil(sceneDepthSource)", "WaterRefractionCapturePass should resolve the renderer supplied depth source.");
        AssertNotContains(pass, "GraphicsDevice.Presenter.DepthStencilBuffer", "WaterRefractionCapturePass should not infer scene depth from the presenter.");
        AssertNotContains(pass, "GetPresenterSceneDepthSource", "WaterRefractionCapturePass should not use a presenter-depth fallback.");
        AssertNotContains(pass, "_RefractionCaptureExposure", "WaterRefractionCapturePass should not bind non-CK3 scene color exposure.");
        AssertNotContains(pass, "_RefractionCaptureColorScale", "WaterRefractionCapturePass should not bind non-CK3 scene color scaling.");
    }

    private static void WaterRefractionCaptureResourcesUseHalfFloatTarget()
    {
        string resourcesPath = GetRepositoryPath("Terrain/Rendering/Water/WaterRefractionCaptureResources.cs");
        TestHarness.Assert(File.Exists(resourcesPath), "Terrain/Rendering/Water/WaterRefractionCaptureResources.cs should exist.");

        string resources = File.ReadAllText(resourcesPath);
        AssertContains(resources, "public sealed class WaterRefractionCaptureResources", "WaterRefractionCaptureResources should use the canonical resource helper name.");
        AssertContains(resources, "PixelFormat.R16G16B16A16_Float", "WaterRefractionCaptureResources should allocate a half-float refraction target.");
    }

    private static void WaterRefractionCaptureShaderPacksRiverWorldSpace()
    {
        string shaderPath = GetRepositoryPath("Terrain/Effects/Water/WaterRefractionCapture.sdsl");
        TestHarness.Assert(File.Exists(shaderPath), "Terrain/Effects/Water/WaterRefractionCapture.sdsl should exist.");

        string shader = File.ReadAllText(shaderPath);
        AssertContains(shader, "shader WaterRefractionCapture : ImageEffectShader, DepthBase, Transformation, RiverCommon", "WaterRefractionCapture shader should inherit the target image/depth/river helpers.");
        AssertContains(shader, "RiverCompressWorldSpace(positionWS.xyz, Eye.xyz)", "WaterRefractionCapture shader should pack camera-relative river world space.");
        AssertContains(shader, "float3 capturedColor = Texture0.Sample(LinearSampler, uv).rgb;", "WaterRefractionCapture should pass scene color RGB through like CK3.");
        AssertNotContains(shader, "hdrColor / (1.0f + hdrColor)", "WaterRefractionCapture should not tone-map scene color RGB.");
        AssertNotContains(shader, "_RefractionCaptureColorScale", "WaterRefractionCapture should not scale compressed scene color RGB.");
        AssertNotContains(shader, "CompressRefractionCaptureColor", "WaterRefractionCapture should not keep a non-CK3 scene color compression helper.");
    }

    private static void WaterRefractionCaptureUsesCurrentRendererDepth()
    {
        string renderer = ReadRepositoryText("Terrain/Rendering/CustomForwardRenderer.cs");
        string pass = ReadRepositoryText("Terrain/Rendering/Water/WaterRefractionCapturePass.cs");

        AssertContains(renderer, "sceneDepth,", "CustomForwardRenderer should pass the current scene depth into WaterRefractionCapturePass.Capture.");
        AssertContains(pass, "sceneDepthSource.ViewWidth != sceneColor.ViewWidth", "WaterRefractionCapturePass should validate depth/color size compatibility.");
        AssertContains(pass, "Water refraction capture depth size", "WaterRefractionCapturePass should fail with a clear size mismatch message.");
    }

    private static void RuntimeCompositorUsesCustomForwardRenderer()
    {
        string compositor = ReadRepositoryText("Terrain/Assets/GraphicsCompositor.sdgfxcomp");

        AssertContains(compositor, "!Terrain.Rendering.CustomForwardRenderer,Terrain", "GraphicsCompositor.sdgfxcomp should reference Terrain.Rendering.CustomForwardRenderer.");
        AssertContains(compositor, "SingleView: !Terrain.Rendering.CustomForwardRenderer,Terrain", "GraphicsCompositor SingleView should not keep a stock ForwardRenderer path for River no-op Draw.");
    }

    private static void RuntimeCompositorRoutesWaterStageThroughCustomRenderer()
    {
        string compositor = ReadRepositoryText("Terrain/Assets/GraphicsCompositor.sdgfxcomp");

        AssertContains(compositor, "Name: Water", "GraphicsCompositor should define a dedicated Water render stage.");
        AssertContains(compositor, "WaterRenderStage: ref!!", "CustomForwardRenderer entries should bind the dedicated Water render stage.");
        AssertContains(compositor, "EffectName: RiverSurface", "RiverRenderFeature should keep a selector so Stride culling/sorting populates Water-stage river nodes.");
        AssertContains(compositor, "EffectName: OceanSurface", "OceanRenderFeature should keep a selector so Stride culling/sorting populates Water-stage ocean nodes.");
        AssertContains(compositor, "RenderStage: ref!! 6b596b72-f95b-4f48-9f75-bf73b61e9fe9", "RiverRenderFeature selector should target the Water render stage.");
        AssertNotContains(compositor, "SingleView: !Stride.Rendering.Compositing.ForwardRenderer", "SingleView should not use the stock ForwardRenderer.");
    }

    private static void WaterRenderingArchitectureAvoidsObsoleteRendererNames()
    {
        string[] obsoleteNames =
        [
            "WaterRefractionCaptureRenderer",
            "WaterRefractionSeed",
            "WaterOrderedForwardRenderer",
            "TerrainForwardRenderer",
            "WaterForwardRenderer",
        ];

        string[] architectureFiles =
        [
            "Terrain/Rendering/CustomForwardRenderer.cs",
            "Terrain/Rendering/Water/WaterRefractionCapturePass.cs",
            "Terrain/Rendering/Water/WaterRefractionCaptureResources.cs",
            "Terrain/Effects/Water/WaterRefractionCapture.sdsl",
            "Terrain/Assets/GraphicsCompositor.sdgfxcomp",
        ];

        foreach (string relativePath in architectureFiles)
        {
            string fullPath = GetRepositoryPath(relativePath);
            if (!File.Exists(fullPath))
                continue;

            string text = File.ReadAllText(fullPath);
            foreach (string obsoleteName in obsoleteNames)
                AssertNotContains(text, obsoleteName, $"{relativePath} should not contain obsolete water architecture name {obsoleteName}.");
        }
    }

    private static void CustomForwardRendererRejectsUnsupportedMsaaOutputs()
    {
        string renderer = ReadRepositoryText("Terrain/Rendering/CustomForwardRenderer.cs");

        AssertContains(renderer, "viewOutputTarget != null && viewOutputTarget.MultisampleCount != MultisampleCount.None", "CustomForwardRenderer should explicitly detect unsupported non-null MSAA color outputs.");
        AssertContains(renderer, "viewDepthStencil != null && viewDepthStencil.MultisampleCount != MultisampleCount.None", "CustomForwardRenderer should explicitly detect unsupported non-null MSAA depth outputs.");
        AssertNotContains(renderer, "viewOutputTarget?.MultisampleCount != MultisampleCount.None", "CustomForwardRenderer should not treat a missing color target as unsupported MSAA.");
        AssertNotContains(renderer, "viewDepthStencil?.MultisampleCount != MultisampleCount.None", "CustomForwardRenderer should not treat a missing depth target as unsupported MSAA.");
        AssertContains(renderer, "CustomForwardRenderer does not support MSAA output targets", "CustomForwardRenderer should fail fast instead of silently dropping MSAA output.");
    }

    private static void CustomForwardRendererIncludesOceanSeaLevelInCaptureClamp()
    {
        string renderer = ReadRepositoryText("Terrain/Rendering/CustomForwardRenderer.cs");
        string oceanFeature = ReadRepositoryText("Terrain/Rendering/Ocean/OceanRenderFeature.cs");

        AssertContains(renderer, "oceanRenderFeature.GetRefractionMaxCameraHeight", "CustomForwardRenderer should include Ocean ranges when resolving the shared refraction capture clamp.");
        AssertOccursBefore(renderer, "float refractionMaxCameraHeight = ResolveWaterRefractionMaxCameraHeight();", "waterRefractionCapturePass.Capture(", "CustomForwardRenderer should resolve the shared Ocean/River capture clamp before issuing the capture.");
        AssertContains(oceanFeature, "internal float GetRefractionMaxCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)", "OceanRenderFeature should expose Ocean range clamp resolution to CustomForwardRenderer.");
        AssertContains(oceanFeature, "oceanObject.SeaLevel + 1.0f", "Ocean refraction capture clamp should include high Ocean sea levels with padding.");
        AssertContains(oceanFeature, "MathF.Max(maxHeight, oceanObject.SeaLevel + 1.0f)", "Ocean refraction capture clamp should keep the largest Ocean sea level in the draw range.");
    }

    private static void OceanRenderFeatureExposesRendererCallableWaterDraw()
    {
        string oceanFeature = ReadRepositoryText("Terrain/Rendering/Ocean/OceanRenderFeature.cs");

        AssertContains(oceanFeature, "DrawWater(", "OceanRenderFeature should expose a renderer-callable DrawWater method.");
        AssertContains(oceanFeature, "Ocean is driven by CustomForwardRenderer", "OceanRenderFeature.Draw should document why the stage callback is inert.");
        AssertContains(oceanFeature, "OceanSurfaceKeys.RefractionTexture", "OceanRenderFeature.DrawWater should bind the shared refraction texture.");
    }

    private static void RiverRenderFeatureExposesRendererCallableWaterChain()
    {
        string riverFeature = ReadRepositoryText("Terrain/Rendering/River/RiverRenderFeature.cs");

        AssertContains(riverFeature, "DrawWaterChain(", "RiverRenderFeature should expose a renderer-callable DrawWaterChain method.");
        AssertContains(riverFeature, "float refractionMaxCameraHeight", "RiverRenderFeature.DrawWaterChain should receive the shared capture clamp.");
        AssertContains(riverFeature, "BindRefractionMaxCameraHeight(bottomEffect, refractionMaxCameraHeight);", "River bottom should decode with the shared capture clamp.");
        AssertContains(riverFeature, "BindRefractionMaxCameraHeight(surfaceEffect, refractionMaxCameraHeight);", "River surface should decode with the shared capture clamp.");
        AssertContains(riverFeature, "internal float GetRiverMaxVisibleCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)", "RiverRenderFeature should expose the draw-range camera visibility cutoff to the renderer.");
        AssertNotContains(riverFeature, "new ImageEffectShader(\"RiverSceneSeed", "RiverRenderFeature should no longer create the legacy RiverSceneSeed image effect directly.");
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
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Terrain.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private static void AssertContains(string text, string expected, string message)
    {
        TestHarness.Assert(text.Contains(expected, StringComparison.Ordinal), message);
    }

    private static void AssertNotContains(string text, string unexpected, string message)
    {
        TestHarness.Assert(!text.Contains(unexpected, StringComparison.Ordinal), message);
    }

    private static void AssertOccursBefore(string text, string first, string second, string message)
    {
        int firstIndex = text.IndexOf(first, StringComparison.Ordinal);
        int secondIndex = text.IndexOf(second, StringComparison.Ordinal);
        TestHarness.Assert(firstIndex >= 0 && secondIndex >= 0 && firstIndex < secondIndex, message);
    }

    private static void AssertOccursExactly(string text, string expected, int count, string message)
    {
        int actual = 0;
        int index = 0;
        while (true)
        {
            index = text.IndexOf(expected, index, StringComparison.Ordinal);
            if (index < 0)
                break;

            actual++;
            index += expected.Length;
        }

        TestHarness.AssertEqual(count, actual, message);
    }
}
