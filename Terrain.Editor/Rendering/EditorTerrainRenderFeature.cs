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
using Stride.Rendering.ComputeEffect;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Stride render feature for editor terrain.
/// Per CONTEXT.md: Independent from core Terrain library.
/// </summary>
public sealed class EditorTerrainRenderFeature : RootEffectRenderFeature
{
    private const string OpaqueStageName = "Opaque";
    private const string TransparentStageName = "Transparent";
    private const string ShadowCasterStageName = "ShadowMapCaster";
    private const string ShadowCasterParaboloidStageName = "ShadowMapCasterParaboloid";
    private const string ShadowCasterCubeMapStageName = "ShadowMapCasterCubeMap";

    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");
    private static readonly ProfilingKey ExtractKey = new("EditorTerrainRenderFeature.Extract");
    private static readonly ProfilingKey PreparePermutationsImplKey = new("EditorTerrainRenderFeature.PreparePermutationsImpl");
    private static readonly ProfilingKey PrepareKey = new("EditorTerrainRenderFeature.Prepare");
    private static readonly ProfilingKey DrawKey = new("EditorTerrainRenderFeature.Draw");

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
    private readonly EditorTerrainComputeDispatcher computeDispatcher = new();
    private bool rebuildingManagedRenderFeatures;

    [DataMember]
    [Category]
    [MemberCollection(CanReorderItems = true, NotNullItems = true)]
    public TrackingCollection<SubRenderFeature> RenderFeatures { get; } = new();

    public override Type SupportedRenderObjectType => typeof(EditorTerrainRenderObject);

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
            var renderMesh = (EditorTerrainRenderObject)renderObject;
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
        var renderMesh = (EditorTerrainRenderObject)renderObject;
        var drawData = renderMesh.ActiveMeshDraw;

        pipelineState.InputElements = PrepareInputElements(pipelineState, drawData);
        pipelineState.PrimitiveType = drawData.PrimitiveType;
        // Disable culling for editor terrain patches
        pipelineState.RasterizerState = new RasterizerStateDescription(CullMode.None);

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

            var renderMesh = (EditorTerrainRenderObject)renderNode.RenderObject;
            var drawData = renderMesh.ActiveMeshDraw;

            var renderEffect = renderNode.RenderEffect;
            if (renderEffect.Effect == null)
            {
                continue;
            }

            // Terrain is drawn by multiple RenderViews in the same frame, and shadow views do not share
            // the main camera frustum, so chunk selection must happen against the view being drawn now.
            PrepareEditorTerrainDraw(context, renderMesh, renderView);

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
                Log.Warning($"Editor terrain render feature fell back to default light renderer configuration: {failureReason}");
            }

            AddDefaultLightRenderers(forwardLighting.LightRenderers);
        }

        var sharedShadowMapRenderer = TryFindMainShadowMapRenderer();
        if (sharedShadowMapRenderer != null)
        {
            forwardLighting.ShadowMapRenderer = new EditorTerrainSharedShadowMapRendererProxy(sharedShadowMapRenderer);
        }
        else
        {
            Log.Warning("Editor terrain render feature could not find the main MeshRenderFeature shadow map renderer. Terrain shadow receiving is disabled.");
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

    private void PrepareEditorTerrainDraw(RenderDrawContext drawContext, EditorTerrainRenderObject renderObject, RenderView renderView)
    {
        var commandList = drawContext.CommandList;
        if (renderObject.Source is not EditorTerrainEntity entity)
        {
            renderObject.InstanceCount = 0;
            return;
        }

        Debug.Assert(entity.ChunkNodeData != null);
        Debug.Assert(renderObject.ChunkNodeBuffer != null);
        Debug.Assert(renderObject.LodLookupBuffer != null);
        Debug.Assert(renderObject.LodLookupLayoutBuffer != null);
        Debug.Assert(renderObject.LodMapTexture != null);

        // Create quad tree if needed
        if (renderObject.QuadTree == null)
        {
            renderObject.QuadTree = new EditorTerrainQuadTree(
                entity.MinMaxErrorMaps!,
                entity.BaseChunkSize,
                entity.HeightmapWidth,
                entity.HeightmapHeight,
                entity.HeightScale,
                entity.MaxScreenSpaceErrorPixels);
        }

        var (renderCount, nodeCount) = renderObject.QuadTree.Select(
            renderObject.World.TranslationVector,
            renderView,
            entity.ChunkNodeData);
        if (nodeCount <= 0)
        {
            return;
        }

        entity.UpdateChunkNodeData(commandList, entity.ChunkNodeData, renderCount, nodeCount);
        if (renderCount <= 0)
        {
            return;
        }

        computeDispatcher.Dispatch(drawContext, renderObject, renderCount, nodeCount, entity.MaxLod);
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

/// <summary>
/// Render object for editor terrain.
/// Similar to TerrainRenderObject but simpler (no streaming).
/// </summary>
public sealed class EditorTerrainRenderObject : RenderMesh, IDisposable
{
    private const int DefaultChunkNodeCapacity = 65536;

    public EditorTerrainEntity? Source;

    // GPU resources (mirroring EditorTerrainEntity's)
    public Texture? HeightmapTexture;
    public Buffer? ChunkNodeBuffer;
    public Buffer? LodLookupBuffer;
    public Buffer? LodLookupLayoutBuffer;
    public Texture? LodMapTexture;
    public Buffer? PatchVertexBuffer;
    public Buffer? PatchIndexBuffer;

    // Quad tree for LOD selection
    internal EditorTerrainQuadTree? QuadTree;

    public void InitializeFromEntity(GraphicsDevice graphicsDevice, EditorTerrainEntity entity)
    {
        Source = entity;

        // Copy GPU resource references from entity
        HeightmapTexture = entity.HeightmapTexture;
        ChunkNodeBuffer = entity.ChunkNodeBuffer;
        LodLookupBuffer = entity.LodLookupBuffer;
        LodLookupLayoutBuffer = entity.LodLookupLayoutBuffer;
        LodMapTexture = entity.LodMapTexture;
        PatchVertexBuffer = entity.PatchVertexBuffer;
        PatchIndexBuffer = entity.PatchIndexBuffer;

        // Create mesh draw from patch geometry
        if (PatchVertexBuffer != null && PatchIndexBuffer != null)
        {
            int vertexCountPerAxis = entity.BaseChunkSize + 1;
            int vertexCount = vertexCountPerAxis * vertexCountPerAxis;
            int indexCount = (entity.BaseChunkSize / 2) * (entity.BaseChunkSize / 2) * 8 * 3;

            var meshDraw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = indexCount,
                StartLocation = 0,
                VertexBuffers =
                [
                    new VertexBufferBinding(PatchVertexBuffer, EditorPatchVertex.Layout, vertexCount),
                ],
                IndexBuffer = new IndexBufferBinding(PatchIndexBuffer, true, indexCount),
            };

            Mesh = new Mesh(meshDraw, new ParameterCollection());
            ActiveMeshDraw = meshDraw;
        }

        World = Matrix.Translation(entity.WorldOffset);
        BoundingBox = (BoundingBoxExt)entity.Bounds;

        ResetRenderState();
    }

    public void UpdateChunkNodeData(CommandList commandList, TerrainChunkNode[] data, int renderCount, int nodeCount)
    {
        Debug.Assert(ChunkNodeBuffer != null);
        if (nodeCount <= 0)
        {
            InstanceCount = 0;
            return;
        }

        ChunkNodeBuffer!.SetData(commandList, new ReadOnlySpan<TerrainChunkNode>(data, 0, nodeCount));
        InstanceCount = renderCount;
    }

    public void ResetRenderState()
    {
        MaterialPass = null!;
        InstanceCount = 0;
    }

    public void Dispose()
    {
        // Note: We don't own the GPU resources - EditorTerrainEntity does
        ResetRenderState();
    }
}

/// <summary>
/// Compute dispatcher for editor terrain LOD map building.
/// Reuses shaders from Terrain project.
/// </summary>
internal sealed class EditorTerrainComputeDispatcher : IDisposable
{
    private const int LookupThreadCountX = 64;
    private const int LodMapThreadCountX = 8;
    private const int LodMapThreadCountY = 8;
    private static readonly ProfilingKey BuildLodLookupKey = new("EditorTerrain.BuildLodLookup");
    private static readonly ProfilingKey BuildLodMapKey = new("EditorTerrain.BuildLodMap");
    private static readonly ProfilingKey BuildNeighborMaskKey = new("EditorTerrain.BuildNeighborMask");

    private ComputeEffectShader? buildLodLookupEffect;
    private ComputeEffectShader? buildLodMapEffect;
    private ComputeEffectShader? buildNeighborMaskEffect;

    public void Initialize(RenderContext renderContext)
    {
        buildLodLookupEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildLodLookup",
            ThreadNumbers = new Int3(LookupThreadCountX, 1, 1),
        };

        buildLodMapEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildLodMap",
            ThreadNumbers = new Int3(LodMapThreadCountX, LodMapThreadCountY, 1),
        };

        buildNeighborMaskEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildNeighborMask",
            ThreadNumbers = new Int3(LookupThreadCountX, 1, 1),
        };
    }

    public void Dispatch(RenderDrawContext drawContext, EditorTerrainRenderObject renderObject, int renderCount, int nodeCount, int maxLod)
    {
        Debug.Assert(buildLodLookupEffect != null);
        Debug.Assert(buildLodMapEffect != null);
        Debug.Assert(buildNeighborMaskEffect != null);
        Debug.Assert(renderObject.ChunkNodeBuffer != null);
        Debug.Assert(renderObject.LodLookupBuffer != null);
        Debug.Assert(renderObject.LodLookupLayoutBuffer != null);
        Debug.Assert(renderObject.LodMapTexture != null);

        if (renderCount <= 0 || nodeCount <= 0)
        {
            return;
        }

        int lookupThreadGroupCountX = (nodeCount + LookupThreadCountX - 1) / LookupThreadCountX;
        int neighborThreadGroupCountX = (renderCount + LookupThreadCountX - 1) / LookupThreadCountX;
        int lodMapThreadGroupCountX = (renderObject.LodMapTexture.Width + LodMapThreadCountX - 1) / LodMapThreadCountX;
        int lodMapThreadGroupCountY = (renderObject.LodMapTexture.Height + LodMapThreadCountY - 1) / LodMapThreadCountY;
        var commandList = drawContext.CommandList;

        commandList.ResourceBarrierTransition(renderObject.ChunkNodeBuffer, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(renderObject.LodLookupLayoutBuffer, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(renderObject.LodLookupBuffer, GraphicsResourceState.UnorderedAccess);
        commandList.ResourceBarrierTransition(renderObject.LodMapTexture, GraphicsResourceState.UnorderedAccess);

        using (Profiler.Begin(BuildLodLookupKey))
        {
            buildLodLookupEffect!.ThreadGroupCounts = new Int3(lookupThreadGroupCountX, 1, 1);
            buildLodLookupEffect.Parameters.Set(Terrain.TerrainBuildLodLookupKeys.ChunkNodeBuffer, renderObject.ChunkNodeBuffer);
            buildLodLookupEffect.Parameters.Set(Terrain.TerrainBuildLodLookupKeys.LodLookupLayoutBuffer, renderObject.LodLookupLayoutBuffer);
            buildLodLookupEffect.Parameters.Set(Terrain.TerrainBuildLodLookupKeys.LodLookupBuffer, renderObject.LodLookupBuffer);
            buildLodLookupEffect.Parameters.Set(Terrain.TerrainBuildLodLookupKeys.NodeCount, nodeCount);
            buildLodLookupEffect.Draw(drawContext);
        }

        commandList.ResourceBarrierTransition(renderObject.LodLookupBuffer, GraphicsResourceState.NonPixelShaderResource);

        using (Profiler.Begin(BuildLodMapKey))
        {
            buildLodMapEffect!.ThreadGroupCounts = new Int3(lodMapThreadGroupCountX, lodMapThreadGroupCountY, 1);
            buildLodMapEffect.Parameters.Set(Terrain.TerrainBuildLodMapKeys.LodLookupBuffer, renderObject.LodLookupBuffer);
            buildLodMapEffect.Parameters.Set(Terrain.TerrainBuildLodMapKeys.LodLookupLayoutBuffer, renderObject.LodLookupLayoutBuffer);
            buildLodMapEffect.Parameters.Set(Terrain.TerrainBuildLodMapKeys.LodMap, renderObject.LodMapTexture);
            buildLodMapEffect.Parameters.Set(Terrain.TerrainBuildLodMapKeys.MaxLod, maxLod);
            buildLodMapEffect.Parameters.Set(Terrain.TerrainBuildLodMapKeys.LodMapWidth, renderObject.LodMapTexture.Width);
            buildLodMapEffect.Parameters.Set(Terrain.TerrainBuildLodMapKeys.LodMapHeight, renderObject.LodMapTexture.Height);
            buildLodMapEffect.Draw(drawContext);
        }

        commandList.ResourceBarrierTransition(renderObject.LodMapTexture, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(renderObject.ChunkNodeBuffer, GraphicsResourceState.UnorderedAccess);

        using (Profiler.Begin(BuildNeighborMaskKey))
        {
            buildNeighborMaskEffect!.ThreadGroupCounts = new Int3(neighborThreadGroupCountX, 1, 1);
            buildNeighborMaskEffect.Parameters.Set(Terrain.TerrainBuildNeighborMaskKeys.InstanceBuffer, renderObject.ChunkNodeBuffer);
            buildNeighborMaskEffect.Parameters.Set(Terrain.TerrainBuildNeighborMaskKeys.LodMap, renderObject.LodMapTexture);
            buildNeighborMaskEffect.Parameters.Set(Terrain.TerrainBuildNeighborMaskKeys.InstanceCount, renderCount);
            buildNeighborMaskEffect.Parameters.Set(Terrain.TerrainBuildNeighborMaskKeys.LodMapWidth, renderObject.LodMapTexture.Width);
            buildNeighborMaskEffect.Parameters.Set(Terrain.TerrainBuildNeighborMaskKeys.LodMapHeight, renderObject.LodMapTexture.Height);
            buildNeighborMaskEffect.Draw(drawContext);
        }

        commandList.ResourceBarrierTransition(renderObject.ChunkNodeBuffer, GraphicsResourceState.NonPixelShaderResource);
    }

    public void Dispose()
    {
        buildLodLookupEffect?.Dispose();
        buildLodLookupEffect = null;

        buildLodMapEffect?.Dispose();
        buildLodMapEffect = null;

        buildNeighborMaskEffect?.Dispose();
        buildNeighborMaskEffect = null;
    }
}

/// <summary>
/// Read-only proxy for shadow maps from the main mesh pipeline.
/// </summary>
internal sealed class EditorTerrainSharedShadowMapRendererProxy : IShadowMapRenderer
{
    private readonly IShadowMapRenderer sharedShadowMapRenderer;

    public EditorTerrainSharedShadowMapRendererProxy(IShadowMapRenderer sharedShadowMapRenderer)
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
    }

    public void PrepareAtlasAsRenderTargets(CommandList commandList)
    {
        sharedShadowMapRenderer.PrepareAtlasAsRenderTargets(commandList);
    }

    public void PrepareAtlasAsShaderResourceViews(CommandList commandList)
    {
        sharedShadowMapRenderer.PrepareAtlasAsShaderResourceViews(commandList);
    }

    public void Flush(RenderDrawContext context)
    {
        // Do not flush shared shadow state from Terrain; the main mesh pipeline owns that lifecycle.
    }
}
