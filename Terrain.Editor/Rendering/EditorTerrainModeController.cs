#nullable enable

using System;
using System.Linq;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering.NativeViewport;

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
    private const string DefaultMeshEffectName = "StrideForwardShadingEffect";

    private GraphicsCompositor? currentGraphicsCompositor;
    private EditorTerrainRenderFeature? terrainRenderFeature;
    private MeshRenderFeature? pathMeshRenderFeature;
    private RenderStage? opaqueStage;
    private RenderStage? wireframeStage;
    private SimpleGroupToRenderStageSelector? opaqueSelector;
    private EditorTerrainWireframeStageSelector? wireframeSelector;
    private SimpleGroupToRenderStageSelector? pathOpaqueSelector;
    private EditorTerrainWireframeStageSelector? pathWireframeSelector;
    private WireframePipelineProcessor? wireframePipelineProcessor;
    private WireframePipelineProcessor? pathWireframePipelineProcessor;
    private SingleStageRenderer? wireframeStageRenderer;

    public void Apply(SceneViewMode mode, bool isPathWireframeEnabled, GraphicsCompositor graphicsCompositor)
    {
        var targetMode = ResolveMode(mode);

        EnsureModeBindings(graphicsCompositor);
        if (!HasModeBindings())
        {
            return;
        }

        ApplyMode(targetMode, isPathWireframeEnabled);
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
            && pathMeshRenderFeature != null
            && graphicsCompositor.RenderFeatures.Contains(pathMeshRenderFeature)
            && pathOpaqueSelector != null
            && pathWireframeSelector != null
            && (terrainRenderFeature.RenderStageSelectors.Contains(opaqueSelector)
                || terrainRenderFeature.RenderStageSelectors.Contains(wireframeSelector))
            && (pathMeshRenderFeature.RenderStageSelectors.Contains(pathOpaqueSelector)
                || pathMeshRenderFeature.RenderStageSelectors.Contains(pathWireframeSelector))
            && wireframePipelineProcessor != null
            && terrainRenderFeature.PipelineProcessors.Contains(wireframePipelineProcessor)
            && pathWireframePipelineProcessor != null
            && pathMeshRenderFeature.PipelineProcessors.Contains(pathWireframePipelineProcessor)
            && AreDefaultMeshSelectorsExcludingPathGroup(pathMeshRenderFeature)
            && wireframeStageRenderer != null
            && IsRendererAttached(graphicsCompositor, wireframeStageRenderer))
        {
            return;
        }

        currentGraphicsCompositor = graphicsCompositor;
        terrainRenderFeature = graphicsCompositor.RenderFeatures.OfType<EditorTerrainRenderFeature>().FirstOrDefault();
        pathMeshRenderFeature = graphicsCompositor.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
        opaqueStage = null;
        wireframeStage = null;
        opaqueSelector = null;
        wireframeSelector = null;
        pathOpaqueSelector = null;
        pathWireframeSelector = null;
        wireframePipelineProcessor = null;
        pathWireframePipelineProcessor = null;
        wireframeStageRenderer = null;

        if (terrainRenderFeature == null || pathMeshRenderFeature == null)
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
        ExcludePathRenderGroupFromDefaultMeshSelectors(pathMeshRenderFeature);
        string pathEffectName = ResolvePathEffectName(pathMeshRenderFeature, opaqueStage);
        pathOpaqueSelector = EnsurePathOpaqueSelector(pathMeshRenderFeature, opaqueStage, pathEffectName);
        pathWireframeSelector = EnsurePathWireframeSelector(pathEffectName, wireframeStage);
        wireframePipelineProcessor = EnsureWireframePipelineProcessor(terrainRenderFeature, wireframeStage);
        pathWireframePipelineProcessor = EnsurePathWireframePipelineProcessor(pathMeshRenderFeature, wireframeStage);
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

        if (!pathMeshRenderFeature.RenderStageSelectors.Contains(pathWireframeSelector))
        {
            pathMeshRenderFeature.RenderStageSelectors.Add(pathWireframeSelector);
        }

        if (!pathMeshRenderFeature.RenderStageSelectors.Contains(pathOpaqueSelector))
        {
            pathMeshRenderFeature.RenderStageSelectors.Add(pathOpaqueSelector);
        }
    }

    private void ApplyMode(EditorTerrainViewMode mode, bool isPathWireframeEnabled)
    {
        bool enableTerrainWireframe = mode == EditorTerrainViewMode.Wireframe;

        ApplySelectorState(
            terrainRenderFeature!,
            opaqueSelector!,
            wireframeSelector!,
            enableTerrainWireframe);
        ApplySelectorState(
            pathMeshRenderFeature!,
            pathOpaqueSelector!,
            pathWireframeSelector!,
            isPathWireframeEnabled);

        wireframeStageRenderer!.RenderStage = enableTerrainWireframe || isPathWireframeEnabled
            ? wireframeStage
            : null;
    }

    private static EditorTerrainViewMode ResolveMode(SceneViewMode mode)
    {
        return mode switch
        {
            SceneViewMode.Wireframe => EditorTerrainViewMode.Wireframe,
            // Perspective is the default 3D editor camera view and maps to shaded terrain rendering.
            SceneViewMode.Perspective => EditorTerrainViewMode.Shaded,
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
            && pathMeshRenderFeature != null
            && pathOpaqueSelector != null
            && pathWireframeSelector != null
            && wireframePipelineProcessor != null
            && pathWireframePipelineProcessor != null
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

    private static string ResolvePathEffectName(MeshRenderFeature meshRenderFeature, RenderStage opaqueStage)
    {
        return meshRenderFeature.RenderStageSelectors
            .OfType<MeshTransparentRenderStageSelector>()
            .FirstOrDefault(item =>
                ReferenceEquals(item.OpaqueRenderStage, opaqueStage)
                || string.Equals(item.OpaqueRenderStage?.Name, opaqueStage.Name, StringComparison.Ordinal))
            ?.EffectName
            ?? DefaultMeshEffectName;
    }

    private static void ExcludePathRenderGroupFromDefaultMeshSelectors(MeshRenderFeature meshRenderFeature)
    {
        foreach (MeshTransparentRenderStageSelector selector in meshRenderFeature.RenderStageSelectors.OfType<MeshTransparentRenderStageSelector>())
        {
            selector.RenderGroup &= ~RenderGroupMask.Group1;
        }
    }

    private static bool AreDefaultMeshSelectorsExcludingPathGroup(MeshRenderFeature meshRenderFeature)
    {
        return meshRenderFeature.RenderStageSelectors
            .OfType<MeshTransparentRenderStageSelector>()
            .All(item => (item.RenderGroup & RenderGroupMask.Group1) == 0);
    }

    private static SimpleGroupToRenderStageSelector EnsurePathOpaqueSelector(
        MeshRenderFeature meshRenderFeature,
        RenderStage opaqueStage,
        string pathEffectName)
    {
        var selector = meshRenderFeature.RenderStageSelectors
            .OfType<SimpleGroupToRenderStageSelector>()
            .FirstOrDefault(item =>
                item.GetType() == typeof(SimpleGroupToRenderStageSelector)
                && string.Equals(item.EffectName, pathEffectName, StringComparison.Ordinal)
                && item.RenderGroup == RenderGroupMask.Group1);

        selector ??= new SimpleGroupToRenderStageSelector
        {
            EffectName = pathEffectName,
            RenderGroup = RenderGroupMask.Group1,
        };

        if (!meshRenderFeature.RenderStageSelectors.Contains(selector))
        {
            meshRenderFeature.RenderStageSelectors.Add(selector);
        }

        selector.RenderStage ??= opaqueStage;
        return selector;
    }

    private static EditorTerrainWireframeStageSelector EnsurePathWireframeSelector(string pathEffectName, RenderStage wireframeStage)
    {
        return new EditorTerrainWireframeStageSelector
        {
            EffectName = pathEffectName,
            RenderGroup = RenderGroupMask.Group1,
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

    private static WireframePipelineProcessor EnsurePathWireframePipelineProcessor(MeshRenderFeature renderFeature, RenderStage wireframeStage)
    {
        var processor = renderFeature.PipelineProcessors
            .OfType<WireframePipelineProcessor>()
            .FirstOrDefault(item =>
                ReferenceEquals(item.RenderStage, wireframeStage)
                || string.Equals(item.RenderStage?.Name, WireframeStageName, StringComparison.Ordinal));

        processor ??= new WireframePipelineProcessor();

        if (!renderFeature.PipelineProcessors.Contains(processor))
        {
            int insertIndex = renderFeature.PipelineProcessors
                .Select(static (item, index) => new { item, index })
                .FirstOrDefault(entry => entry.item is PathDepthBiasPipelineProcessor)
                ?.index ?? renderFeature.PipelineProcessors.Count;
            renderFeature.PipelineProcessors.Insert(insertIndex, processor);
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
        if (graphicsCompositor.Game is PresenterViewportSceneRenderer presenterRenderer)
        {
            return EnsureSceneRendererCollection(presenterRenderer);
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

    private static SceneRendererCollection? EnsureSceneRendererCollection(PresenterViewportSceneRenderer presenterRenderer)
    {
        if (presenterRenderer.Child is SceneRendererCollection sceneRendererCollection)
        {
            return sceneRendererCollection;
        }

        if (presenterRenderer.Child is SceneCameraRenderer sceneCameraRenderer)
        {
            return EnsureSceneRendererCollection(sceneCameraRenderer);
        }

        if (presenterRenderer.Child is ISceneRenderer sceneRenderer)
        {
            var wrappedCollection = new SceneRendererCollection();
            wrappedCollection.Children.Add(sceneRenderer);
            presenterRenderer.Child = wrappedCollection;
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
        RootRenderFeature renderFeature,
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
