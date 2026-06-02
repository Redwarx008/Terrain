#nullable enable

using System;
using System.Linq;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering.NativeViewport;
using Terrain.Editor.Services;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Applies editor SceneViewMode wireframe rendering to generated river mesh entities.
/// Rivers are regular ModelComponent meshes, so we isolate them with a dedicated render group
/// and route only that group through a dedicated wireframe stage while the global scene view is Wireframe.
/// </summary>
public sealed class RiverWireframeModeController
{
    private const string OpaqueStageName = "Opaque";
    private const string TransparentStageName = "Transparent";
    private const string WireframeStageName = "EditorRiverWireframe";
    private const string WireframeRendererName = "Editor River Wireframe Renderer";
    private const string DefaultMeshEffectName = "StrideForwardShadingEffect";
    private const RenderGroupMask NonRiverRenderGroupMask = RenderGroupMask.All & ~RiverRenderingService.RiverRenderGroupMask;

    private GraphicsCompositor? currentGraphicsCompositor;
    private MeshRenderFeature? meshRenderFeature;
    private RenderStage? opaqueStage;
    private RenderStage? transparentStage;
    private RenderStage? wireframeStage;
    private MeshTransparentRenderStageSelector? defaultMeshSelector;
    private SimpleGroupToRenderStageSelector? wireframeSelector;
    private WireframePipelineProcessor? wireframePipelineProcessor;
    private SingleStageRenderer? wireframeStageRenderer;

    public void Apply(SceneViewMode mode, GraphicsCompositor graphicsCompositor)
    {
        EnsureModeBindings(graphicsCompositor);
        if (!HasModeBindings())
        {
            return;
        }

        bool enableWireframe = mode == SceneViewMode.Wireframe;
        ApplySelectorState(meshRenderFeature!, defaultMeshSelector!, wireframeSelector!, enableWireframe);
        wireframeStageRenderer!.RenderStage = enableWireframe ? wireframeStage : null;
    }

    private void EnsureModeBindings(GraphicsCompositor graphicsCompositor)
    {
        if (ReferenceEquals(currentGraphicsCompositor, graphicsCompositor)
            && meshRenderFeature != null
            && graphicsCompositor.RenderFeatures.Contains(meshRenderFeature)
            && opaqueStage != null
            && graphicsCompositor.RenderStages.Contains(opaqueStage)
            && transparentStage != null
            && graphicsCompositor.RenderStages.Contains(transparentStage)
            && wireframeStage != null
            && graphicsCompositor.RenderStages.Contains(wireframeStage)
            && defaultMeshSelector != null
            && meshRenderFeature.RenderStageSelectors.Contains(defaultMeshSelector)
            && wireframeSelector != null
            && wireframePipelineProcessor != null
            && meshRenderFeature.PipelineProcessors.Contains(wireframePipelineProcessor)
            && wireframeStageRenderer != null
            && IsRendererAttached(graphicsCompositor, wireframeStageRenderer))
        {
            return;
        }

        currentGraphicsCompositor = graphicsCompositor;
        meshRenderFeature = graphicsCompositor.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
        opaqueStage = null;
        transparentStage = null;
        wireframeStage = null;
        defaultMeshSelector = null;
        wireframeSelector = null;
        wireframePipelineProcessor = null;
        wireframeStageRenderer = null;

        if (meshRenderFeature == null)
        {
            return;
        }

        opaqueStage = graphicsCompositor.RenderStages.FirstOrDefault(stage =>
            string.Equals(stage.Name, OpaqueStageName, StringComparison.Ordinal));
        transparentStage = graphicsCompositor.RenderStages.FirstOrDefault(stage =>
            string.Equals(stage.Name, TransparentStageName, StringComparison.Ordinal));
        if (opaqueStage == null || transparentStage == null)
        {
            return;
        }

        defaultMeshSelector = EnsureDefaultMeshSelector(meshRenderFeature, opaqueStage, transparentStage);
        wireframeStage = EnsureWireframeStage(graphicsCompositor, opaqueStage);
        wireframeSelector = EnsureWireframeSelector(meshRenderFeature, wireframeStage);
        wireframePipelineProcessor = EnsureWireframePipelineProcessor(meshRenderFeature, wireframeStage);
        wireframeStageRenderer = EnsureWireframeStageRenderer(graphicsCompositor);
    }

    private bool HasModeBindings()
    {
        return meshRenderFeature != null
            && opaqueStage != null
            && transparentStage != null
            && wireframeStage != null
            && defaultMeshSelector != null
            && wireframeSelector != null
            && wireframePipelineProcessor != null
            && wireframeStageRenderer != null;
    }

    private static MeshTransparentRenderStageSelector EnsureDefaultMeshSelector(
        MeshRenderFeature renderFeature,
        RenderStage opaqueStage,
        RenderStage transparentStage)
    {
        var selector = renderFeature.RenderStageSelectors
            .OfType<MeshTransparentRenderStageSelector>()
            .FirstOrDefault(item =>
                string.Equals(item.EffectName, DefaultMeshEffectName, StringComparison.Ordinal)
                && ReferenceEquals(item.OpaqueRenderStage, opaqueStage)
                && ReferenceEquals(item.TransparentRenderStage, transparentStage));

        selector ??= new MeshTransparentRenderStageSelector();
        selector.EffectName = DefaultMeshEffectName;
        selector.OpaqueRenderStage = opaqueStage;
        selector.TransparentRenderStage = transparentStage;
        selector.RenderGroup = RenderGroupMask.All;

        if (!renderFeature.RenderStageSelectors.Contains(selector))
        {
            renderFeature.RenderStageSelectors.Add(selector);
        }

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

    private static SimpleGroupToRenderStageSelector EnsureWireframeSelector(MeshRenderFeature renderFeature, RenderStage wireframeStage)
    {
        var selector = renderFeature.RenderStageSelectors
            .OfType<SimpleGroupToRenderStageSelector>()
            .FirstOrDefault(item =>
                item.GetType() == typeof(SimpleGroupToRenderStageSelector)
                && string.Equals(item.EffectName, DefaultMeshEffectName, StringComparison.Ordinal)
                && item.RenderGroup == RiverRenderingService.RiverRenderGroupMask
                && ReferenceEquals(item.RenderStage, wireframeStage));

        selector ??= new SimpleGroupToRenderStageSelector();
        selector.EffectName = DefaultMeshEffectName;
        selector.RenderGroup = RiverRenderingService.RiverRenderGroupMask;
        selector.RenderStage = wireframeStage;
        return selector;
    }

    private static WireframePipelineProcessor EnsureWireframePipelineProcessor(MeshRenderFeature renderFeature, RenderStage wireframeStage)
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
        MeshRenderFeature renderFeature,
        MeshTransparentRenderStageSelector defaultSelector,
        SimpleGroupToRenderStageSelector wireframeSelector,
        bool enableWireframe)
    {
        defaultSelector.RenderGroup = enableWireframe ? NonRiverRenderGroupMask : RenderGroupMask.All;

        if (enableWireframe)
        {
            if (!renderFeature.RenderStageSelectors.Contains(wireframeSelector))
            {
                renderFeature.RenderStageSelectors.Add(wireframeSelector);
            }
        }
        else if (renderFeature.RenderStageSelectors.Contains(wireframeSelector))
        {
            renderFeature.RenderStageSelectors.Remove(wireframeSelector);
        }
    }
}
