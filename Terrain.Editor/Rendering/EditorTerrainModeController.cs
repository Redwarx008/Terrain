#nullable enable

using System;
using System.Linq;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Terrain.Editor.UI.Panels;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Central scene-view mode controller for editor terrain rendering.
/// Wireframe is implemented first; additional modes can extend this state machine later.
/// </summary>
public sealed class EditorTerrainModeController
{
    private const string OpaqueStageName = "Opaque";
    private const string WireframeStageName = "EditorTerrainWireframe";
    private const string WireframeRendererName = "Editor Terrain Wireframe Renderer";

    private GraphicsCompositor? currentGraphicsCompositor;
    private EditorTerrainRenderFeature? terrainRenderFeature;
    private RenderStage? opaqueStage;
    private RenderStage? wireframeStage;
    private SimpleGroupToRenderStageSelector? opaqueSelector;
    private EditorTerrainWireframeStageSelector? wireframeSelector;
    private WireframePipelineProcessor? wireframePipelineProcessor;
    private SingleStageRenderer? wireframeStageRenderer;

    public void Apply(SceneViewMode mode, GraphicsCompositor graphicsCompositor)
    {
        var targetMode = ResolveMode(mode);

        EnsureModeBindings(graphicsCompositor);
        if (!HasModeBindings())
        {
            return;
        }

        ApplyMode(targetMode);
    }

    private void EnsureModeBindings(GraphicsCompositor graphicsCompositor)
    {
        if (ReferenceEquals(currentGraphicsCompositor, graphicsCompositor)
            && terrainRenderFeature != null
            && graphicsCompositor.RenderFeatures.Contains(terrainRenderFeature)
            && opaqueStage != null
            && graphicsCompositor.RenderStages.Contains(opaqueStage)
            && wireframeStage != null
            && graphicsCompositor.RenderStages.Contains(wireframeStage)
            && opaqueSelector != null
            && wireframeSelector != null
            && (terrainRenderFeature.RenderStageSelectors.Contains(opaqueSelector)
                || terrainRenderFeature.RenderStageSelectors.Contains(wireframeSelector))
            && wireframePipelineProcessor != null
            && terrainRenderFeature.PipelineProcessors.Contains(wireframePipelineProcessor)
            && wireframeStageRenderer != null
            && IsRendererAttached(graphicsCompositor, wireframeStageRenderer))
        {
            return;
        }

        currentGraphicsCompositor = graphicsCompositor;
        terrainRenderFeature = graphicsCompositor.RenderFeatures.OfType<EditorTerrainRenderFeature>().FirstOrDefault();
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

        opaqueStage = graphicsCompositor.RenderStages.FirstOrDefault(stage =>
            string.Equals(stage.Name, OpaqueStageName, StringComparison.Ordinal));
        if (opaqueStage == null)
        {
            return;
        }

        opaqueSelector = EnsureOpaqueSelector(terrainRenderFeature, opaqueStage);
        wireframeStage = EnsureWireframeStage(graphicsCompositor, opaqueStage);
        wireframeSelector = EnsureWireframeSelector(opaqueSelector.RenderGroup, wireframeStage);
        wireframePipelineProcessor = EnsureWireframePipelineProcessor(terrainRenderFeature, wireframeStage);
        wireframeStageRenderer = EnsureWireframeStageRenderer(graphicsCompositor);

        // Ensure selector instances are registered once; ApplyMode controls which selector stays active.
        if (!terrainRenderFeature.RenderStageSelectors.Contains(wireframeSelector))
        {
            terrainRenderFeature.RenderStageSelectors.Add(wireframeSelector);
        }

        if (!terrainRenderFeature.RenderStageSelectors.Contains(opaqueSelector))
        {
            terrainRenderFeature.RenderStageSelectors.Add(opaqueSelector);
        }
    }

    private void ApplyMode(EditorTerrainViewMode mode)
    {
        bool enableWireframe = mode == EditorTerrainViewMode.Wireframe;

        ApplySelectorState(
            terrainRenderFeature!,
            opaqueSelector!,
            wireframeSelector!,
            enableWireframe);

        wireframeStageRenderer!.RenderStage = enableWireframe ? wireframeStage : null;
    }

    private static EditorTerrainViewMode ResolveMode(SceneViewMode mode)
    {
        return mode switch
        {
            SceneViewMode.Wireframe => EditorTerrainViewMode.Wireframe,
            // Current behavior: Textured falls back to shaded until a dedicated textured path exists.
            SceneViewMode.Shaded => EditorTerrainViewMode.Shaded,
            SceneViewMode.Textured => EditorTerrainViewMode.Shaded,
            _ => EditorTerrainViewMode.Shaded,
        };
    }

    private bool HasModeBindings()
    {
        return terrainRenderFeature != null
            && opaqueStage != null
            && wireframeStage != null
            && opaqueSelector != null
            && wireframeSelector != null
            && wireframePipelineProcessor != null
            && wireframeStageRenderer != null;
    }

    private static SimpleGroupToRenderStageSelector EnsureOpaqueSelector(EditorTerrainRenderFeature renderFeature, RenderStage opaqueStage)
    {
        var selector = renderFeature.RenderStageSelectors
            .OfType<SimpleGroupToRenderStageSelector>()
            .FirstOrDefault(item =>
                item.GetType() == typeof(SimpleGroupToRenderStageSelector)
                && string.Equals(item.EffectName, EditorTerrainRenderFeature.EffectName, StringComparison.Ordinal));

        selector ??= new SimpleGroupToRenderStageSelector
        {
            EffectName = EditorTerrainRenderFeature.EffectName,
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
        var stage = graphicsCompositor.RenderStages.FirstOrDefault(item =>
            string.Equals(item.Name, WireframeStageName, StringComparison.Ordinal));
        if (stage == null)
        {
            stage = new RenderStage(WireframeStageName, "Main");
            graphicsCompositor.RenderStages.Add(stage);
        }

        stage.SortMode = opaqueStage.SortMode;
        return stage;
    }

    private static EditorTerrainWireframeStageSelector EnsureWireframeSelector(RenderGroupMask renderGroup, RenderStage wireframeStage)
    {
        return new EditorTerrainWireframeStageSelector
        {
            EffectName = EditorTerrainRenderFeature.EffectName,
            RenderGroup = renderGroup,
            RenderStage = wireframeStage,
        };
    }

    private static WireframePipelineProcessor EnsureWireframePipelineProcessor(EditorTerrainRenderFeature renderFeature, RenderStage wireframeStage)
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
        if (graphicsCompositor.Game is ViewportRenderTextureSceneRenderer viewportRenderer)
        {
            return EnsureSceneRendererCollection(viewportRenderer);
        }

        if (graphicsCompositor.Game is SceneRendererCollection sceneRendererCollection)
        {
            return sceneRendererCollection;
        }

        if (graphicsCompositor.Game is SceneCameraRenderer sceneCameraRenderer)
        {
            return EnsureSceneRendererCollection(sceneCameraRenderer);
        }

        if (graphicsCompositor.Game is ISceneRenderer sceneRenderer)
        {
            var wrappedCollection = new SceneRendererCollection();
            wrappedCollection.Children.Add(sceneRenderer);
            graphicsCompositor.Game = wrappedCollection;
            return wrappedCollection;
        }

        return null;
    }

    private static SceneRendererCollection? EnsureSceneRendererCollection(ViewportRenderTextureSceneRenderer viewportRenderer)
    {
        if (viewportRenderer.Child is SceneRendererCollection sceneRendererCollection)
        {
            return sceneRendererCollection;
        }

        if (viewportRenderer.Child is SceneCameraRenderer sceneCameraRenderer)
        {
            return EnsureSceneRendererCollection(sceneCameraRenderer);
        }

        if (viewportRenderer.Child is ISceneRenderer sceneRenderer)
        {
            var wrappedCollection = new SceneRendererCollection();
            wrappedCollection.Children.Add(sceneRenderer);
            viewportRenderer.Child = wrappedCollection;
            return wrappedCollection;
        }

        return null;
    }

    private static SceneRendererCollection EnsureSceneRendererCollection(SceneCameraRenderer sceneCameraRenderer)
    {
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

    private static void ApplySelectorState(
        EditorTerrainRenderFeature renderFeature,
        SimpleGroupToRenderStageSelector opaqueSelector,
        EditorTerrainWireframeStageSelector wireframeSelector,
        bool enableWireframe)
    {
        // VisibilityGroup reevaluates active stages when the selector collection changes,
        // so mode transitions must swap selectors in/out.
        if (enableWireframe)
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

    private enum EditorTerrainViewMode
    {
        Shaded,
        Wireframe,
    }
}

public sealed class EditorTerrainWireframeStageSelector : SimpleGroupToRenderStageSelector
{
}
