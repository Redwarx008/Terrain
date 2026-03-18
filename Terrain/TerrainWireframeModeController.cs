#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace Terrain;

public sealed class TerrainWireframeModeController : SyncScript
{
    private const string OpaqueStageName = "Opaque";
    private const string WireframeStageName = "TerrainWireframe";
    private const string WireframeRendererName = "Terrain Wireframe Renderer";

    private GraphicsCompositor? currentGraphicsCompositor;
    private TerrainRenderFeature? terrainRenderFeature;
    private RenderStage? opaqueStage;
    private RenderStage? wireframeStage;
    private SimpleGroupToRenderStageSelector? opaqueSelector;
    private TerrainWireframeStageSelector? wireframeSelector;
    private WireframePipelineProcessor? wireframePipelineProcessor;
    private SingleStageRenderer? wireframeStageRenderer;
    private bool isWireframeEnabled;

    public Keys ToggleKey { get; set; } = Keys.D1;

    public Int2 DebugTextPosition { get; set; } = new(20, 20);

    public string DebugLabel { get; set; } = "Terrain Wireframe";

    public override void Update()
    {
        EnsureWireframeBindings();
        HandleToggleInput();
        ApplyWireframeMode();
        DrawDebugOverlay();
    }

    private void EnsureWireframeBindings()
    {
        var graphicsCompositor = SceneSystem?.GraphicsCompositor;
        if (graphicsCompositor == null)
        {
            terrainRenderFeature = null;
            opaqueStage = null;
            wireframeStage = null;
            opaqueSelector = null;
            wireframeSelector = null;
            wireframePipelineProcessor = null;
            wireframeStageRenderer = null;
            currentGraphicsCompositor = null;
            return;
        }

        if (ReferenceEquals(currentGraphicsCompositor, graphicsCompositor)
            && terrainRenderFeature != null
            && graphicsCompositor.RenderFeatures.Contains(terrainRenderFeature)
            && opaqueStage != null
            && graphicsCompositor.RenderStages.Contains(opaqueStage)
            && wireframeStage != null
            && graphicsCompositor.RenderStages.Contains(wireframeStage)
            && opaqueSelector != null
            && terrainRenderFeature.RenderStageSelectors.Contains(opaqueSelector)
            && wireframeSelector != null
            && terrainRenderFeature.RenderStageSelectors.Contains(wireframeSelector)
            && wireframePipelineProcessor != null
            && terrainRenderFeature.PipelineProcessors.Contains(wireframePipelineProcessor)
            && wireframeStageRenderer != null
            && IsRendererAttached(graphicsCompositor, wireframeStageRenderer))
        {
            return;
        }

        currentGraphicsCompositor = graphicsCompositor;
        terrainRenderFeature = graphicsCompositor.RenderFeatures.OfType<TerrainRenderFeature>().FirstOrDefault();
        opaqueStage = null;
        wireframeStage = null;
        opaqueSelector = null;
        wireframeSelector = null;
        wireframePipelineProcessor = null;
        wireframeStageRenderer = null;

        if (terrainRenderFeature == null)
        {
            return;
        }

        opaqueStage = graphicsCompositor.RenderStages.FirstOrDefault(stage => string.Equals(stage.Name, OpaqueStageName, StringComparison.Ordinal));
        if (opaqueStage == null)
        {
            return;
        }

        // Keep TerrainRenderFeature itself ignorant of "wireframe mode". We attach a dedicated
        // render stage, selector, pipeline processor, and stage renderer from the outside, similar
        // to Stride's editor/debug services.
        opaqueSelector = EnsureOpaqueSelector(terrainRenderFeature, opaqueStage);
        wireframeStage = EnsureWireframeStage(graphicsCompositor, opaqueStage);
        wireframeSelector = EnsureWireframeSelector(opaqueSelector.RenderGroup, wireframeStage);
        wireframePipelineProcessor = EnsureWireframePipelineProcessor(terrainRenderFeature, wireframeStage);
        wireframeStageRenderer = EnsureWireframeStageRenderer(graphicsCompositor);
        ApplySelectorState(terrainRenderFeature, opaqueSelector, wireframeSelector, isWireframeEnabled);
    }

    private void HandleToggleInput()
    {
        if (!Input.HasKeyboard || !Input.IsKeyPressed(ToggleKey))
        {
            return;
        }

        EnsureWireframeBindings();
        if (!HasWireframeBindings())
        {
            return;
        }

        AssertWireframeBindings();

        isWireframeEnabled = !isWireframeEnabled;

        var status = isWireframeEnabled ? "ON" : "OFF";
        var color = isWireframeEnabled ? Color.LightGreen : Color.Orange;
        var confirmationPosition = new Int2(DebugTextPosition.X, DebugTextPosition.Y + 40);

        DebugText.Print($"{DebugLabel}: {status}", confirmationPosition, color, TimeSpan.FromSeconds(1.5));
    }

    private void ApplyWireframeMode()
    {
        if (!HasWireframeBindings())
        {
            return;
        }

        AssertWireframeBindings();
        ApplySelectorState(terrainRenderFeature!, opaqueSelector!, wireframeSelector!, isWireframeEnabled);
        wireframeStageRenderer!.RenderStage = isWireframeEnabled ? wireframeStage : null;
    }

    private void DrawDebugOverlay()
    {
        var isAvailable = terrainRenderFeature != null
            && opaqueStage != null
            && wireframeStage != null
            && opaqueSelector != null
            && wireframeSelector != null
            && wireframePipelineProcessor != null
            && wireframeStageRenderer != null;
        var status = !isAvailable
            ? "UNAVAILABLE"
            : isWireframeEnabled ? "ON" : "OFF";
        var statusColor = !isAvailable
            ? Color.OrangeRed
            : isWireframeEnabled ? Color.LightGreen : Color.White;

        // DebugText keeps the most recent messages, so we print the persistent overlay every frame.
        DebugText.Print("Terrain Debug", DebugTextPosition, Color.White);
        DebugText.Print($"[{GetKeyLabel(ToggleKey)}] {DebugLabel}: {status}", new Int2(DebugTextPosition.X, DebugTextPosition.Y + 20), statusColor);
    }

    private static string GetKeyLabel(Keys key)
    {
        return key switch
        {
            Keys.D0 => "0",
            Keys.D1 => "1",
            Keys.D2 => "2",
            Keys.D3 => "3",
            Keys.D4 => "4",
            Keys.D5 => "5",
            Keys.D6 => "6",
            Keys.D7 => "7",
            Keys.D8 => "8",
            Keys.D9 => "9",
            _ => key.ToString(),
        };
    }

    private static SimpleGroupToRenderStageSelector EnsureOpaqueSelector(TerrainRenderFeature renderFeature, RenderStage opaqueStage)
    {
        var selector = renderFeature.RenderStageSelectors
            .OfType<SimpleGroupToRenderStageSelector>()
            .FirstOrDefault(item =>
                item.GetType() == typeof(SimpleGroupToRenderStageSelector)
                && string.Equals(item.EffectName, TerrainRenderFeature.EffectName, StringComparison.Ordinal));

        selector ??= new SimpleGroupToRenderStageSelector
        {
            EffectName = TerrainRenderFeature.EffectName,
            RenderGroup = RenderGroupMask.Group0,
        };

        if (!renderFeature.RenderStageSelectors.Contains(selector))
        {
            renderFeature.RenderStageSelectors.Add(selector);
        }

        selector.RenderStage ??= opaqueStage;
        return selector;
    }

    private static RenderStage EnsureWireframeStage(GraphicsCompositor graphicsCompositor, RenderStage opaqueStage)
    {
        var stage = graphicsCompositor.RenderStages.FirstOrDefault(item => string.Equals(item.Name, WireframeStageName, StringComparison.Ordinal));
        if (stage == null)
        {
            stage = new RenderStage(WireframeStageName, "Main");
            graphicsCompositor.RenderStages.Add(stage);
        }

        stage.SortMode = opaqueStage.SortMode;
        return stage;
    }

    private static TerrainWireframeStageSelector EnsureWireframeSelector(RenderGroupMask renderGroup, RenderStage wireframeStage)
    {
        var selector = new TerrainWireframeStageSelector
        {
            EffectName = TerrainRenderFeature.EffectName,
            RenderGroup = renderGroup,
            RenderStage = wireframeStage,
        };

        return selector;
    }

    private static WireframePipelineProcessor EnsureWireframePipelineProcessor(TerrainRenderFeature renderFeature, RenderStage wireframeStage)
    {
        var processor = renderFeature.PipelineProcessors
            .OfType<WireframePipelineProcessor>()
            .FirstOrDefault(item =>
                ReferenceEquals(item.RenderStage, wireframeStage)
                || string.Equals(item.RenderStage?.Name, WireframeStageName, StringComparison.Ordinal));

        processor ??= new WireframePipelineProcessor();

        if (!renderFeature.PipelineProcessors.Contains(processor))
        {
            renderFeature.PipelineProcessors.Add(processor);
        }

        processor.RenderStage = wireframeStage;
        return processor;
    }

    private static SingleStageRenderer? EnsureWireframeStageRenderer(GraphicsCompositor graphicsCompositor)
    {
        var sceneRendererCollection = EnsureSceneRendererCollection(graphicsCompositor);
        if (sceneRendererCollection == null)
        {
            return null;
        }

        var renderer = sceneRendererCollection.Children
            .OfType<SingleStageRenderer>()
            .FirstOrDefault(item => string.Equals(item.Name, WireframeRendererName, StringComparison.Ordinal));

        renderer ??= new SingleStageRenderer();

        if (!sceneRendererCollection.Children.Contains(renderer))
        {
            sceneRendererCollection.Children.Insert(Math.Min(1, sceneRendererCollection.Children.Count), renderer);
        }

        renderer.Name = WireframeRendererName;
        renderer.RenderStage = null;
        return renderer;
    }

    private static SceneRendererCollection? EnsureSceneRendererCollection(GraphicsCompositor graphicsCompositor)
    {
        if (graphicsCompositor.Game is SceneRendererCollection sceneRendererCollection)
        {
            return sceneRendererCollection;
        }

        if (graphicsCompositor.Game is not SceneCameraRenderer sceneCameraRenderer)
        {
            return null;
        }

        if (sceneCameraRenderer.Child is SceneRendererCollection childCollection)
        {
            return childCollection;
        }

        var wrappedCollection = new SceneRendererCollection();
        if (sceneCameraRenderer.Child != null)
        {
            wrappedCollection.Children.Add(sceneCameraRenderer.Child);
        }

        sceneCameraRenderer.Child = wrappedCollection;
        return wrappedCollection;
    }

    private static bool IsRendererAttached(GraphicsCompositor graphicsCompositor, SingleStageRenderer renderer)
    {
        var sceneRendererCollection = EnsureSceneRendererCollection(graphicsCompositor);
        return sceneRendererCollection != null && sceneRendererCollection.Children.Contains(renderer);
    }

    private bool HasWireframeBindings()
    {
        return terrainRenderFeature != null
            && opaqueStage != null
            && wireframeStage != null
            && opaqueSelector != null
            && wireframeSelector != null
            && wireframePipelineProcessor != null
            && wireframeStageRenderer != null;
    }

    [Conditional("DEBUG")]
    private void AssertWireframeBindings()
    {
        Debug.Assert(terrainRenderFeature != null);
        Debug.Assert(opaqueStage != null);
        Debug.Assert(wireframeStage != null);
        Debug.Assert(opaqueSelector != null);
        Debug.Assert(wireframeSelector != null);
        Debug.Assert(wireframePipelineProcessor != null);
        Debug.Assert(wireframeStageRenderer != null);
    }

    private static void ApplySelectorState(
        TerrainRenderFeature renderFeature,
        SimpleGroupToRenderStageSelector opaqueSelector,
        TerrainWireframeStageSelector wireframeSelector,
        bool isWireframeEnabled)
    {
        // Important: toggling selector properties alone is not enough. VisibilityGroup only
        // reevaluates active render stages when the RenderStageSelectors collection changes, so we
        // swap selectors in/out of the collection to move Terrain between Opaque and Wireframe.
        if (isWireframeEnabled)
        {
            if (renderFeature.RenderStageSelectors.Contains(opaqueSelector))
            {
                renderFeature.RenderStageSelectors.Remove(opaqueSelector);
            }

            if (!renderFeature.RenderStageSelectors.Contains(wireframeSelector))
            {
                renderFeature.RenderStageSelectors.Add(wireframeSelector);
            }
        }
        else
        {
            if (renderFeature.RenderStageSelectors.Contains(wireframeSelector))
            {
                renderFeature.RenderStageSelectors.Remove(wireframeSelector);
            }

            if (!renderFeature.RenderStageSelectors.Contains(opaqueSelector))
            {
                renderFeature.RenderStageSelectors.Add(opaqueSelector);
            }
        }
    }
}
