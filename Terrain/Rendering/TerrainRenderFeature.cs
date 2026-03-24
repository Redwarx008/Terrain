#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Collections;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Threading;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.LightProbes;
using Stride.Rendering.Materials;
using Stride.Rendering.Shadows;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain;

public sealed class TerrainRenderFeature : RootEffectRenderFeature
{
    private const string OpaqueStageName = "Opaque";
    private const string TransparentStageName = "Transparent";
    private const string ShadowCasterStageName = "ShadowMapCaster";
    private const string ShadowCasterParaboloidStageName = "ShadowMapCasterParaboloid";
    private const string ShadowCasterCubeMapStageName = "ShadowMapCasterCubeMap";

    private static readonly Logger Log = GlobalLogger.GetLogger("Quantum");
    private static readonly ProfilingKey ExtractKey = new("TerrainRenderFeature.Extract");
    private static readonly ProfilingKey PreparePermutationsImplKey = new("TerrainRenderFeature.PreparePermutationsImpl");
    private static readonly ProfilingKey PrepareKey = new("TerrainRenderFeature.Prepare");
    private static readonly ProfilingKey DrawKey = new("TerrainRenderFeature.Draw");

    private readonly ThreadLocal<DescriptorSet[]> descriptorSets = new();
    private Buffer? emptyBuffer;
    private static readonly MethodInfo? AttachRootRenderFeatureMethod = typeof(SubRenderFeature).GetMethod("AttachRootRenderFeature", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? RootRenderFeatureField = typeof(SubRenderFeature).GetField("RootRenderFeature", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? RenderSystemProperty = typeof(RenderFeature).GetProperty("RenderSystem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public const string EffectName = "TerrainForwardShadingEffect";
    public const string ShadowCasterEffectName = $"{EffectName}.ShadowMapCaster";
    public const string ShadowCasterParaboloidEffectName = $"{EffectName}.ShadowMapCasterParaboloid";
    public const string ShadowCasterCubeMapEffectName = $"{EffectName}.ShadowMapCasterCubeMap";

    private MeshPipelineProcessor? meshPipelineProcessor;
    private ShadowMeshPipelineProcessor? shadowMapPipelineProcessor;
    private ShadowMeshPipelineProcessor? shadowParaboloidPipelineProcessor;
    private ShadowMeshPipelineProcessor? shadowCubeMapPipelineProcessor;
    private readonly TerrainComputeDispatcher computeDispatcher = new();
    private bool rebuildingManagedRenderFeatures;

    [DataMember]
    [Category]
    [MemberCollection(CanReorderItems = true, NotNullItems = true)]
    public TrackingCollection<SubRenderFeature> RenderFeatures { get; } = new();

    public override Type SupportedRenderObjectType => typeof(TerrainRenderObject);

    protected override void InitializeCore()
    {
        base.InitializeCore();

        RenderFeatures.CollectionChanged += RenderFeatures_CollectionChanged;
        EnsureConfiguredSubRenderFeatures();
        EnsureDefaultPipelineProcessors();
           

        foreach (var renderFeature in RenderFeatures)
        {
            BindSubRenderFeature(renderFeature);
            renderFeature.Initialize(Context);
        }

        emptyBuffer = Buffer.Vertex.New(Context.GraphicsDevice, new Vector4[1]);
        SyncRenderStageBindings();
        SyncPipelineBindings();
    }

    protected override void Destroy()
    {
        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.Dispose();
        }

        RenderFeatures.CollectionChanged -= RenderFeatures_CollectionChanged;
        descriptorSets.Dispose();
        computeDispatcher.Dispose();

        emptyBuffer?.Dispose();
        emptyBuffer = null;

        base.Destroy();
    }

    protected override void OnRenderSystemChanged()
    {
        base.OnRenderSystemChanged();
        SyncRenderStageBindings();
        SyncPipelineBindings();
    }

    public override void Collect()
    {
        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.Collect();
        }
    }

    public override void Extract()
    {
        using var _ = Profiler.Begin(ExtractKey);
        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.Extract();
        }
    }

    public override void PrepareEffectPermutationsImpl(RenderDrawContext context)
    {
        using var _ = Profiler.Begin(PreparePermutationsImplKey);

        Dispatcher.ForEach(RenderObjects, renderObject =>
        {
            var renderMesh = (TerrainRenderObject)renderObject;
            renderMesh.ActiveMeshDraw = renderMesh.Mesh.Draw;
        });

        base.PrepareEffectPermutationsImpl(context);

        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.PrepareEffectPermutations(context);
        }
    }

    public override void Prepare(RenderDrawContext context)
    {
        using var _ = Profiler.Begin(PrepareKey);

        computeDispatcher.Initialize(context.RenderContext);
        base.Prepare(context);

        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.Prepare(context);
        }
    }

    protected override void ProcessPipelineState(RenderContext context, RenderNodeReference renderNodeReference, ref RenderNode renderNode, RenderObject renderObject, PipelineStateDescription pipelineState)
    {
        var renderMesh = (TerrainRenderObject)renderObject;
        var drawData = renderMesh.ActiveMeshDraw;

        pipelineState.InputElements = PrepareInputElements(pipelineState, drawData);
        pipelineState.PrimitiveType = drawData.PrimitiveType;

        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.ProcessPipelineState(context, renderNodeReference, ref renderNode, renderObject, pipelineState);
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage)
    {
        using var _ = Profiler.Begin(DrawKey);
        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.Draw(context, renderView, renderViewStage);
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        using var _ = Profiler.Begin(DrawKey);
        var commandList = context.CommandList;

        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.Draw(context, renderView, renderViewStage, startIndex, endIndex);
        }

        var descriptorSetsLocal = descriptorSets.Value;
        if (descriptorSetsLocal == null || descriptorSetsLocal.Length < EffectDescriptorSetSlotCount)
        {
            descriptorSetsLocal = descriptorSets.Value = new DescriptorSet[EffectDescriptorSetSlotCount];
        }

        MeshDraw? currentDrawData = null;
        int emptyBufferSlot = -1;
        for (int index = startIndex; index < endIndex; index++)
        {
            var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
            var renderNode = GetRenderNode(renderNodeReference);

            var renderMesh = (TerrainRenderObject)renderNode.RenderObject;
            var drawData = renderMesh.ActiveMeshDraw;

            var renderEffect = renderNode.RenderEffect;
            if (renderEffect.Effect == null)
            {
                continue;
            }

            // Terrain is drawn by multiple RenderViews in the same frame, and shadow views do not share
            // the main camera frustum, so chunk selection must happen against the view being drawn now.
            PrepareTerrainDraw(context, renderMesh, renderView);

            if (!ReferenceEquals(currentDrawData, drawData))
            {
                for (int slot = 0; slot < drawData.VertexBuffers.Length; slot++)
                {
                    var vertexBuffer = drawData.VertexBuffers[slot];
                    commandList.SetVertexBuffer(slot, vertexBuffer.Buffer, vertexBuffer.Offset, vertexBuffer.Stride);
                }

                if (emptyBuffer != null && emptyBufferSlot != drawData.VertexBuffers.Length)
                {
                    commandList.SetVertexBuffer(drawData.VertexBuffers.Length, emptyBuffer, 0, 0);
                    emptyBufferSlot = drawData.VertexBuffers.Length;
                }

                if (drawData.IndexBuffer != null)
                {
                    commandList.SetIndexBuffer(drawData.IndexBuffer.Buffer, drawData.IndexBuffer.Offset, drawData.IndexBuffer.Is32Bit);
                }

                currentDrawData = drawData;
            }

            var resourceGroupOffset = ComputeResourceGroupOffset(renderNodeReference);
            renderEffect.Reflection.BufferUploader.Apply(commandList, ResourceGroupPool, resourceGroupOffset);

            for (int i = 0; i < descriptorSetsLocal.Length; ++i)
            {
                var resourceGroup = ResourceGroupPool[resourceGroupOffset++];
                if (resourceGroup != null)
                {
                    descriptorSetsLocal[i] = resourceGroup.DescriptorSet;
                }
            }

            commandList.SetPipelineState(renderEffect.PipelineState);
            commandList.SetDescriptorSets(0, descriptorSetsLocal);

            if (drawData.IndexBuffer == null || renderMesh.InstanceCount <= 0)
            {
                continue;
            }

            commandList.DrawIndexedInstanced(drawData.DrawCount, renderMesh.InstanceCount, drawData.StartLocation);
        }
    }

    public override void Flush(RenderDrawContext context)
    {
        base.Flush(context);

        foreach (var renderFeature in RenderFeatures)
        {
            renderFeature.Flush(context);
        }
    }

    private void EnsureConfiguredSubRenderFeatures()
    {
        var unmanagedFeatures = RenderFeatures
            .Where(feature => !IsManagedSubRenderFeature(feature))
            .ToList();

        rebuildingManagedRenderFeatures = true;
        try
        {
            RenderFeatures.Clear();

            foreach (var renderFeature in CreateManagedSubRenderFeatures())
            {
                RenderFeatures.Add(renderFeature);
            }

            foreach (var renderFeature in unmanagedFeatures)
            {
                RenderFeatures.Add(renderFeature);
            }
        }
        finally
        {
            rebuildingManagedRenderFeatures = false;
        }
    }

    private IEnumerable<SubRenderFeature> CreateManagedSubRenderFeatures()
    {
        yield return new TransformRenderFeature();
        yield return new MaterialRenderFeature();
        yield return new ShadowCasterRenderFeature();
        yield return CreateForwardLightingFeatureFromMeshTemplateOrDefault();
    }

    private MeshRenderFeature? TryFindMeshRenderFeatureTemplate()
    {
        if (RenderSystem == null)
        {
            return null;
        }

        return RenderSystem.RenderFeatures
            .OfType<MeshRenderFeature>()
            .FirstOrDefault();
    }

    private static bool IsManagedSubRenderFeature(SubRenderFeature renderFeature)
    {
        return renderFeature is TransformRenderFeature
            or MaterialRenderFeature
            or ForwardLightingRenderFeature
            or ShadowCasterRenderFeature;
    }

    // Terrain still clones the mesh pipeline's light renderer configuration so it shades with the same
    // light groups as regular meshes. It no longer owns a private ShadowMapRenderer, because Stride's
    // ForwardRenderer only drives shadow-map rendering from the main MeshRenderFeature shadow system.
    private ForwardLightingRenderFeature CreateForwardLightingFeatureFromMeshTemplateOrDefault()
    {
        var forwardLighting = new ForwardLightingRenderFeature();
        var failureReason = string.Empty;
        var source = TryFindMeshRenderFeatureTemplate()?
            .RenderFeatures
            .OfType<ForwardLightingRenderFeature>()
            .FirstOrDefault();

        if (source != null && TryCreateLightRenderers(source, out var lightRenderers, out failureReason))
        {
            foreach (var lightRenderer in lightRenderers)
            {
                forwardLighting.LightRenderers.Add(lightRenderer);
            }
        }
        else
        {
            if (source != null)
            {
                Log.Warning($"Terrain render feature fell back to default light renderer configuration: {failureReason}");
            }

            AddDefaultLightRenderers(forwardLighting.LightRenderers);
        }

        var sharedShadowMapRenderer = TryFindMainShadowMapRenderer();
        if (sharedShadowMapRenderer != null)
        {
            forwardLighting.ShadowMapRenderer = new TerrainSharedShadowMapRendererProxy(sharedShadowMapRenderer);
        }
        else
        {
            Log.Warning("Terrain render feature could not find the main MeshRenderFeature shadow map renderer. Terrain shadow receiving is disabled.");
        }

        return forwardLighting;
    }

    private bool TryCreateLightRenderers(ForwardLightingRenderFeature source, out List<LightGroupRendererBase> lightRenderers, out string failureReason)
    {
        lightRenderers = new List<LightGroupRendererBase>(source.LightRenderers.Count);

        foreach (var renderer in source.LightRenderers)
        {
            var clonedRenderer = CreateLightRenderer(renderer);
            if (clonedRenderer == null)
            {
                failureReason = $"unsupported light renderer '{renderer.GetType().FullName}' in mesh template";
                lightRenderers.Clear();
                return false;
            }

            lightRenderers.Add(clonedRenderer);
        }

        failureReason = string.Empty;
        return true;
    }

    private LightGroupRendererBase? CreateLightRenderer(LightGroupRendererBase renderer)
    {
        return renderer switch
        {
            LightAmbientRenderer => new LightAmbientRenderer(),
            LightDirectionalGroupRenderer => new LightDirectionalGroupRenderer(),
            LightSkyboxRenderer => new LightSkyboxRenderer(),
            LightProbeRenderer => new LightProbeRenderer(),
            LightPointGroupRenderer => new LightPointGroupRenderer(),
            LightSpotGroupRenderer => new LightSpotGroupRenderer(),
            LightClusteredPointSpotGroupRenderer => new LightClusteredPointSpotGroupRenderer(),
            _ => null,
        };
    }

    private void AddDefaultLightRenderers(ICollection<LightGroupRendererBase> lightRenderers)
    {
        lightRenderers.Add(new LightAmbientRenderer());
        lightRenderers.Add(new LightDirectionalGroupRenderer());
        lightRenderers.Add(new LightSkyboxRenderer());
        lightRenderers.Add(new LightClusteredPointSpotGroupRenderer());
        lightRenderers.Add(new LightPointGroupRenderer());
        lightRenderers.Add(new LightSpotGroupRenderer());
        lightRenderers.Add(new LightProbeRenderer());
    }

    // This looks up the shadow renderer that is actually driven by ForwardRenderer at runtime.
    // Terrain shadow receiving depends on that mesh shadow system collecting first, so compositor order
    // must keep MeshRenderFeature ahead of TerrainRenderFeature.
    private IShadowMapRenderer? TryFindMainShadowMapRenderer()
    {
        return TryFindMeshRenderFeatureTemplate()?
            .RenderFeatures
            .OfType<ForwardLightingRenderFeature>()
            .FirstOrDefault()?
            .ShadowMapRenderer;
    }

    private RenderStage? MapShadowStage(RenderStage? sourceStage, string defaultStageName)
    {
        return MapShadowStage(sourceStage?.Name, defaultStageName);
    }

    private RenderStage? MapShadowStage(string? stageName = null, string defaultStageName = ShadowCasterStageName)
    {
        return (stageName ?? defaultStageName) switch
        {
            ShadowCasterParaboloidStageName => FindStage(ShadowCasterParaboloidStageName),
            ShadowCasterCubeMapStageName => FindStage(ShadowCasterCubeMapStageName),
            ShadowCasterStageName => FindStage(ShadowCasterStageName),
            _ => FindStage(defaultStageName),
        };
    }

    private void EnsureDefaultPipelineProcessors()
    {
        meshPipelineProcessor ??= PipelineProcessors.OfType<MeshPipelineProcessor>().FirstOrDefault();
        if (meshPipelineProcessor == null)
        {
            meshPipelineProcessor = new MeshPipelineProcessor();
            PipelineProcessors.Add(meshPipelineProcessor);
        }

        var shadowProcessors = PipelineProcessors.OfType<ShadowMeshPipelineProcessor>().ToList();

        shadowMapPipelineProcessor ??= shadowProcessors.FirstOrDefault(processor =>
            string.Equals(processor.ShadowMapRenderStage?.Name, ShadowCasterStageName, StringComparison.Ordinal))
            ?? shadowProcessors.FirstOrDefault(processor => processor.ShadowMapRenderStage == null && !processor.DepthClipping);
        if (shadowMapPipelineProcessor == null)
        {
            shadowMapPipelineProcessor = new ShadowMeshPipelineProcessor { DepthClipping = false };
            PipelineProcessors.Add(shadowMapPipelineProcessor);
        }

        shadowParaboloidPipelineProcessor ??= shadowProcessors.FirstOrDefault(processor =>
            string.Equals(processor.ShadowMapRenderStage?.Name, ShadowCasterParaboloidStageName, StringComparison.Ordinal))
            ?? shadowProcessors.FirstOrDefault(processor => processor != shadowMapPipelineProcessor && processor.ShadowMapRenderStage == null && processor.DepthClipping);
        if (shadowParaboloidPipelineProcessor == null)
        {
            shadowParaboloidPipelineProcessor = new ShadowMeshPipelineProcessor { DepthClipping = true };
            PipelineProcessors.Add(shadowParaboloidPipelineProcessor);
        }

        shadowCubeMapPipelineProcessor ??= shadowProcessors.FirstOrDefault(processor =>
            string.Equals(processor.ShadowMapRenderStage?.Name, ShadowCasterCubeMapStageName, StringComparison.Ordinal))
            ?? shadowProcessors.FirstOrDefault(processor => processor != shadowMapPipelineProcessor && processor != shadowParaboloidPipelineProcessor && processor.ShadowMapRenderStage == null && processor.DepthClipping);
        if (shadowCubeMapPipelineProcessor == null)
        {
            shadowCubeMapPipelineProcessor = new ShadowMeshPipelineProcessor { DepthClipping = true };
            PipelineProcessors.Add(shadowCubeMapPipelineProcessor);
        }
    }

    private void RenderFeatures_CollectionChanged(object? sender, TrackingCollectionChangedEventArgs e)
    {
        if (e.Item is not SubRenderFeature renderFeature)
        {
            return;
        }

        switch (e.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                BindSubRenderFeature(renderFeature);
                renderFeature.Initialize(Context);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                if (!rebuildingManagedRenderFeatures)
                {
                    renderFeature.Dispose();
                }
                break;
        }
    }

    private void BindSubRenderFeature(SubRenderFeature renderFeature)
    {
        if (AttachRootRenderFeatureMethod != null)
        {
            AttachRootRenderFeatureMethod.Invoke(renderFeature, new object[] { this });
            return;
        }

        if (RootRenderFeatureField == null || RenderSystemProperty == null)
        {
            throw new InvalidOperationException(
                $"Unable to bind sub render feature '{renderFeature.GetType().FullName}' because the required Stride binding hooks are unavailable.");
        }

        RootRenderFeatureField?.SetValue(renderFeature, this);
        RenderSystemProperty?.SetValue(renderFeature, RenderSystem);
    }

    private void SyncRenderStageBindings()
    {
        if (RenderSystem == null)
        {
            return;
        }
        SyncOpaqueSelector();
        SyncShadowSelector(ShadowCasterStageName, ShadowCasterEffectName);
        SyncShadowSelector(ShadowCasterParaboloidStageName, ShadowCasterParaboloidEffectName);
        SyncShadowSelector(ShadowCasterCubeMapStageName, ShadowCasterCubeMapEffectName);
    }

    private void SyncOpaqueSelector()
    {
        var opaqueStage = FindStage(OpaqueStageName);
        var selector = RenderStageSelectors.OfType<SimpleGroupToRenderStageSelector>()
            .FirstOrDefault(item => string.Equals(item.EffectName, EffectName, StringComparison.Ordinal));

        if (opaqueStage == null)
        {
            if (selector != null)
            {
                RenderStageSelectors.Remove(selector);
            }

            return;
        }

        selector ??= new SimpleGroupToRenderStageSelector();
        if (!RenderStageSelectors.Contains(selector))
        {
            RenderStageSelectors.Add(selector);
        }

        selector.RenderGroup = RenderGroupMask.Group0;
        selector.RenderStage = opaqueStage;
        selector.EffectName = EffectName;
    }

    private void SyncShadowSelector(string stageName, string effectName)
    {
        var shadowStage = FindStage(stageName);
        var selector = RenderStageSelectors.OfType<ShadowMapRenderStageSelector>()
            .FirstOrDefault(item => string.Equals(item.EffectName, effectName, StringComparison.Ordinal));

        if (shadowStage == null)
        {
            if (selector != null)
            {
                RenderStageSelectors.Remove(selector);
            }

            return;
        }

        selector ??= new ShadowMapRenderStageSelector();
        if (!RenderStageSelectors.Contains(selector))
        {
            RenderStageSelectors.Add(selector);
        }

        selector.RenderGroup = RenderGroupMask.Group0;
        selector.ShadowMapRenderStage = shadowStage;
        selector.EffectName = effectName;
    }

    private void SyncPipelineBindings()
    {
        if (RenderSystem == null)
        {
            return;
        }

        meshPipelineProcessor ??= PipelineProcessors.OfType<MeshPipelineProcessor>().FirstOrDefault();
        if (meshPipelineProcessor != null)
        {
            meshPipelineProcessor.TransparentRenderStage = FindStage(TransparentStageName);
        }

        shadowMapPipelineProcessor ??= PipelineProcessors.OfType<ShadowMeshPipelineProcessor>().FirstOrDefault(processor =>
            string.Equals(processor.ShadowMapRenderStage?.Name, ShadowCasterStageName, StringComparison.Ordinal));
        if (shadowMapPipelineProcessor != null)
        {
            shadowMapPipelineProcessor.DepthClipping = false;
            shadowMapPipelineProcessor.ShadowMapRenderStage = FindStage(ShadowCasterStageName);
        }

        shadowParaboloidPipelineProcessor ??= PipelineProcessors.OfType<ShadowMeshPipelineProcessor>().FirstOrDefault(processor =>
            string.Equals(processor.ShadowMapRenderStage?.Name, ShadowCasterParaboloidStageName, StringComparison.Ordinal));
        if (shadowParaboloidPipelineProcessor != null)
        {
            shadowParaboloidPipelineProcessor.DepthClipping = true;
            shadowParaboloidPipelineProcessor.ShadowMapRenderStage = FindStage(ShadowCasterParaboloidStageName);
        }

        shadowCubeMapPipelineProcessor ??= PipelineProcessors.OfType<ShadowMeshPipelineProcessor>().FirstOrDefault(processor =>
            string.Equals(processor.ShadowMapRenderStage?.Name, ShadowCasterCubeMapStageName, StringComparison.Ordinal));
        if (shadowCubeMapPipelineProcessor != null)
        {
            shadowCubeMapPipelineProcessor.DepthClipping = true;
            shadowCubeMapPipelineProcessor.ShadowMapRenderStage = FindStage(ShadowCasterCubeMapStageName);
        }
    }

    private RenderStage? FindStage(string stageName)
    {
        return RenderSystem?.RenderStages.FirstOrDefault(stage => string.Equals(stage.Name, stageName, StringComparison.Ordinal));
    }

    private void PrepareTerrainDraw(RenderDrawContext drawContext, TerrainRenderObject renderObject, RenderView renderView)
    {
        var commandList = drawContext.CommandList;
        if (renderObject.Source is not TerrainComponent component || !component.IsInitialized)
        {
            renderObject.InstanceCount = 0;
            return;
        }

        Debug.Assert(component.QuadTree != null);
        Debug.Assert(component.ChunkNodeData.Length > 0);
        Debug.Assert(renderObject.ChunkNodeBuffer != null);
        Debug.Assert(renderObject.LodLookupBuffer != null);
        Debug.Assert(renderObject.LodLookupLayoutBuffer != null);
        Debug.Assert(renderObject.LodMapTexture != null);

        var (renderCount, nodeCount) = component.QuadTree.Select(
            renderObject.World.TranslationVector,
            renderView,
            component.ChunkNodeData);
        if (nodeCount <= 0)
        {
            return;
        }

        renderObject.UpdateChunkNodeData(commandList, component.ChunkNodeData, renderCount, nodeCount);
        if (renderCount <= 0)
        {
            return;
        }

        computeDispatcher.Dispatch(drawContext, renderObject, renderCount, nodeCount, component.MaxLod);
    }

    private static InputElementDescription[] PrepareInputElements(PipelineStateDescription pipelineState, MeshDraw drawData)
    {
        var availableInputElements = drawData.VertexBuffers.CreateInputElements();
        var inputElements = new List<InputElementDescription>(availableInputElements);

        foreach (var inputAttribute in pipelineState.EffectBytecode.Reflection.InputAttributes)
        {
            if (FindElementBySemantic(availableInputElements, inputAttribute.SemanticName, inputAttribute.SemanticIndex) >= 0)
            {
                continue;
            }

            inputElements.Add(new InputElementDescription
            {
                AlignedByteOffset = 0,
                Format = PixelFormat.R32G32B32A32_Float,
                InputSlot = drawData.VertexBuffers.Length,
                InputSlotClass = InputClassification.Vertex,
                InstanceDataStepRate = 0,
                SemanticIndex = inputAttribute.SemanticIndex,
                SemanticName = inputAttribute.SemanticName,
            });
        }

        return inputElements.ToArray();
    }

    private static int FindElementBySemantic(InputElementDescription[] inputElements, string semanticName, int semanticIndex)
    {
        int foundDescIndex = -1;
        for (int index = 0; index < inputElements.Length; index++)
        {
            if (semanticName == inputElements[index].SemanticName && semanticIndex == inputElements[index].SemanticIndex)
            {
                foundDescIndex = index;
            }
        }

        return foundDescIndex;
    }
}

// This adapter is intentionally read-only from Terrain's point of view: it lets Terrain lighting query
// shadow maps produced by the main mesh pipeline, but it is not the owner of shadow-map allocation,
// rendering, or lifetime management.
internal sealed class TerrainSharedShadowMapRendererProxy : IShadowMapRenderer
{
    private readonly IShadowMapRenderer sharedShadowMapRenderer;

    public TerrainSharedShadowMapRendererProxy(IShadowMapRenderer sharedShadowMapRenderer)
    {
        this.sharedShadowMapRenderer = sharedShadowMapRenderer;
    }

    public RenderSystem RenderSystem
    {
        get => sharedShadowMapRenderer.RenderSystem;
        set => sharedShadowMapRenderer.RenderSystem = value;
    }

    public HashSet<RenderView> RenderViewsWithShadows => sharedShadowMapRenderer.RenderViewsWithShadows;

    public List<ILightShadowMapRenderer> Renderers => sharedShadowMapRenderer.Renderers;

    public LightShadowMapTexture FindShadowMap(RenderView renderView, RenderLight light)
    {
        return sharedShadowMapRenderer.FindShadowMap(renderView, light);
    }

    public void Collect(RenderContext context, Dictionary<RenderView, ForwardLightingRenderFeature.RenderViewLightData> renderViewLightDatas)
    {
        foreach (var renderViewData in renderViewLightDatas)
        {
            // Do not allocate atlases or create shadow render views here. The main mesh shadow system has
            // already done that work; Terrain only remaps those finished LightShadowMapTexture entries into
            // its own per-view lighting data.
            renderViewData.Value.RenderLightsWithShadows.Clear();

            foreach (var light in renderViewData.Value.VisibleLightsWithShadows)
            {
                var shadowMap = sharedShadowMapRenderer.FindShadowMap(renderViewData.Key, light);
                if (shadowMap != null)
                {
                    renderViewData.Value.RenderLightsWithShadows[light] = shadowMap;
                }
            }
        }
    }

    public void Draw(RenderDrawContext drawContext)
    {
        // Shadow-map drawing is owned by the main ForwardRenderer -> MeshRenderFeature path.
        // Doing anything here would risk drawing the same shared renderer twice.
    }

    public void PrepareAtlasAsRenderTargets(CommandList commandList)
    {
        // Forward these calls so the shadow/lighting contract still sees the main atlas resources, even
        // though Terrain itself does not own or manage atlas allocation.
        sharedShadowMapRenderer.PrepareAtlasAsRenderTargets(commandList);
    }

    public void PrepareAtlasAsShaderResourceViews(CommandList commandList)
    {
        // Forward these calls so the shadow/lighting contract still sees the main atlas resources, even
        // though Terrain itself does not own or manage atlas allocation.
        sharedShadowMapRenderer.PrepareAtlasAsShaderResourceViews(commandList);
    }

    public void Flush(RenderDrawContext context)
    {
        // Do not flush shared shadow state from Terrain; the main mesh pipeline owns that lifecycle.
    }
}
