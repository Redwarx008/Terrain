#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private ComputeEffectShader? buildLodMapEffect;
    private ComputeEffectShader? buildNeighborMaskEffect;

    private const int ComputeThreadCountX = 64;

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

        buildLodMapEffect?.Dispose();
        buildLodMapEffect = null;
        buildNeighborMaskEffect?.Dispose();
        buildNeighborMaskEffect = null;

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

        BuildComputeEffects(context.RenderContext);
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
                renderFeature.Dispose();
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
        if (renderObject.Source is not TerrainComponent component
            || component.MinMaxErrorMaps == null
            || component.InstanceCapacity <= 0
            || component.InstanceData.Length == 0
            || renderObject.InstanceBuffer == null
            || renderObject.HeightmapArray == null
            || renderObject.LodMapTexture == null)
        {
            renderObject.InstanceCount = 0;
            return;
        }

        int instanceCount = SelectChunks(renderObject.World, component, renderView, component.InstanceData, out bool truncated);
        renderObject.UpdateInstanceData(commandList, component.InstanceData, instanceCount);
        if (instanceCount <= 0)
        {
            return;
        }

        DispatchTerrainCompute(drawContext, renderObject, instanceCount);
    }

    private void DispatchTerrainCompute(RenderDrawContext drawContext, TerrainRenderObject renderObject, int instanceCount)
    {
        if (renderObject.InstanceBuffer == null || renderObject.LodMapTexture == null || instanceCount <= 0 || buildLodMapEffect == null || buildNeighborMaskEffect == null)
        {
            return;
        }

        int threadGroupCountX = (instanceCount + ComputeThreadCountX - 1) / ComputeThreadCountX;
        if (threadGroupCountX <= 0)
        {
            return;
        }

        drawContext.CommandList.ResourceBarrierTransition(renderObject.InstanceBuffer, GraphicsResourceState.NonPixelShaderResource);
        drawContext.CommandList.ResourceBarrierTransition(renderObject.LodMapTexture, GraphicsResourceState.UnorderedAccess);

        buildLodMapEffect!.ThreadGroupCounts = new Int3(threadGroupCountX, 1, 1);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.InstanceBuffer, renderObject.InstanceBuffer);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMap, renderObject.LodMapTexture);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.InstanceCount, instanceCount);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMapWidth, renderObject.LodMapTexture.Width);
        buildLodMapEffect.Parameters.Set(TerrainBuildLodMapKeys.LodMapHeight, renderObject.LodMapTexture.Height);
        buildLodMapEffect.Draw(drawContext);

        drawContext.CommandList.ResourceBarrierTransition(renderObject.LodMapTexture, GraphicsResourceState.NonPixelShaderResource);
        drawContext.CommandList.ResourceBarrierTransition(renderObject.InstanceBuffer, GraphicsResourceState.UnorderedAccess);

        buildNeighborMaskEffect!.ThreadGroupCounts = new Int3(threadGroupCountX, 1, 1);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.InstanceBuffer, renderObject.InstanceBuffer);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMap, renderObject.LodMapTexture);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.InstanceCount, instanceCount);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMapWidth, renderObject.LodMapTexture.Width);
        buildNeighborMaskEffect.Parameters.Set(TerrainBuildNeighborMaskKeys.LodMapHeight, renderObject.LodMapTexture.Height);
        buildNeighborMaskEffect.Draw(drawContext);

        drawContext.CommandList.ResourceBarrierTransition(renderObject.InstanceBuffer, GraphicsResourceState.NonPixelShaderResource);
    }

    private void BuildComputeEffects(RenderContext renderContext)
    {
        buildLodMapEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildLodMap",
            ThreadNumbers = new Int3(ComputeThreadCountX, 1, 1),
        };

        buildNeighborMaskEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainBuildNeighborMask",
            ThreadNumbers = new Int3(ComputeThreadCountX, 1, 1),
        };
    }

    private int SelectChunks(Matrix terrainWorldMatrix, TerrainComponent component, RenderView renderView, TerrainChunkInstance[] instanceData, out bool truncated)
    {
        if (component.MinMaxErrorMaps == null || component.StreamingManager == null)
        {
            truncated = false;
            return 0;
        }

        float viewHeight = Math.Max(1.0f, renderView.ViewSize.Y);
        float screenSpaceScale = viewHeight * 0.5f * MathF.Abs(renderView.Projection.M22);
        Matrix.Invert(ref renderView.View, out var viewInverse);
        var cameraPosition = viewInverse.TranslationVector;
        var topMap = component.MinMaxErrorMaps[component.MaxLod];
        int instanceCapacity = Math.Min(component.InstanceCapacity, instanceData.Length);
        truncated = false;

        int selectedCount = 0;
        for (int y = 0; y < topMap.Height; y++)
        {
            for (int x = 0; x < topMap.Width; x++)
            {
                TraverseChunk(
                    terrainWorldMatrix,
                    component,
                    cameraPosition,
                    renderView.Frustum,
                    screenSpaceScale,
                    component.MaxScreenSpaceErrorPixels,
                    component.StreamingManager,
                    x,
                    y,
                    component.MaxLod,
                    instanceCapacity,
                    instanceData,
                    ref truncated,
                    ref selectedCount);
            }
        }

        if (truncated)
        {
            Log.Warning(
                $"Terrain chunk selection truncated by instance budget, missing patches may occur. " +
                $"selectedCount={selectedCount}, instanceCapacity={instanceCapacity}, maxVisibleChunkInstances={component.MaxVisibleChunkInstances}, renderViewIndex={renderView.Index}, renderView=\"{renderView}\", " +
                $"heightmap={component.HeightmapWidth}x{component.HeightmapHeight}, baseChunkSize={component.BaseChunkSize}, maxScreenSpaceErrorPixels={component.MaxScreenSpaceErrorPixels}.");
        }

        return selectedCount;
    }

    private static void TraverseChunk(
        Matrix terrainWorldMatrix,
        TerrainComponent component,
        Vector3 cameraPosition,
        BoundingFrustum frustum,
        float screenSpaceScale,
        float maxErrorPixels,
        TerrainStreamingManager streamingManager,
        int chunkX,
        int chunkY,
        int lodLevel,
        int instanceCapacity,
        TerrainChunkInstance[] instanceData,
        ref bool truncated,
        ref int selectedCount)
    {
        if (selectedCount >= instanceCapacity)
        {
            truncated = true;
            return;
        }

        int sizeInSamples = component.BaseChunkSize << lodLevel;
        int originSampleX = chunkX * sizeInSamples;
        int originSampleY = chunkY * sizeInSamples;
        if (originSampleX >= component.HeightmapWidth - 1 || originSampleY >= component.HeightmapHeight - 1)
        {
            return;
        }

        var minMaxErrorMap = component.MinMaxErrorMaps![lodLevel];
        minMaxErrorMap.Get(chunkX, chunkY, out var minHeight, out var maxHeight, out var geometricError);
        var bounds = ComputeWorldBounds(terrainWorldMatrix, component, originSampleX, originSampleY, sizeInSamples, minHeight, maxHeight);
        var boundsExt = (BoundingBoxExt)bounds;
        if (!frustum.Contains(ref boundsExt))
        {
            return;
        }

        var key = new TerrainChunkKey(lodLevel, chunkX, chunkY);
        bool isResident = streamingManager.TryGetResidentPageForChunk(key, out int sliceIndex, out int pageOffsetX, out int pageOffsetY, out int pageTexelStride);
        if (!isResident)
        {
            streamingManager.RequestChunk(key, pinned: lodLevel == component.MaxLod);
        }

        float distance = DistanceToAabb(cameraPosition, bounds);
        float sse = distance > 1e-4f
            ? screenSpaceScale * (geometricError * component.HeightScale * TerrainComponent.HeightSampleNormalization) / distance
            : float.MaxValue;
        if (lodLevel == 0 || sse <= maxErrorPixels)
        {
            if (isResident)
            {
                EmitChunkInstance(chunkX, chunkY, lodLevel, sliceIndex, pageOffsetX, pageOffsetY, pageTexelStride, instanceData, ref selectedCount);
            }
            return;
        }

        var childMap = component.MinMaxErrorMaps[lodLevel - 1];
        childMap.GetSubNodesExist(chunkX, chunkY, out var subTLExist, out var subTRExist, out var subBLExist, out var subBRExist);

        bool allChildrenResident = streamingManager.AreChildrenResident(chunkX, chunkY, lodLevel);
        if (!allChildrenResident)
        {
            streamingManager.RequestChildren(chunkX, chunkY, lodLevel);
            if (isResident)
            {
                EmitChunkInstance(chunkX, chunkY, lodLevel, sliceIndex, pageOffsetX, pageOffsetY, pageTexelStride, instanceData, ref selectedCount);
            }
            return;
        }

        int childChunkX = chunkX * 2;
        int childChunkY = chunkY * 2;
        if (subTLExist)
        {
            TraverseChunk(terrainWorldMatrix, component, cameraPosition, frustum, screenSpaceScale, maxErrorPixels, streamingManager, childChunkX, childChunkY, lodLevel - 1, instanceCapacity, instanceData, ref truncated, ref selectedCount);
        }

        if (subTRExist)
        {
            TraverseChunk(terrainWorldMatrix, component, cameraPosition, frustum, screenSpaceScale, maxErrorPixels, streamingManager, childChunkX + 1, childChunkY, lodLevel - 1, instanceCapacity, instanceData, ref truncated, ref selectedCount);
        }

        if (subBLExist)
        {
            TraverseChunk(terrainWorldMatrix, component, cameraPosition, frustum, screenSpaceScale, maxErrorPixels, streamingManager, childChunkX, childChunkY + 1, lodLevel - 1, instanceCapacity, instanceData, ref truncated, ref selectedCount);
        }

        if (subBRExist)
        {
            TraverseChunk(terrainWorldMatrix, component, cameraPosition, frustum, screenSpaceScale, maxErrorPixels, streamingManager, childChunkX + 1, childChunkY + 1, lodLevel - 1, instanceCapacity, instanceData, ref truncated, ref selectedCount);
        }
    }

    private static void EmitChunkInstance(int chunkX, int chunkY, int lodLevel, int sliceIndex, int pageOffsetX, int pageOffsetY, int pageTexelStride, TerrainChunkInstance[] instanceData, ref int selectedCount)
    {
        instanceData[selectedCount++] = new TerrainChunkInstance
        {
            ChunkInfo = new Int4(chunkX, chunkY, lodLevel, 0),
            StreamInfo = new Int4(sliceIndex, pageOffsetX, pageOffsetY, pageTexelStride),
        };
    }

    private static float DistanceToAabb(Vector3 point, BoundingBox bounds)
    {
        float dx = MathF.Max(MathF.Max(bounds.Minimum.X - point.X, 0.0f), point.X - bounds.Maximum.X);
        float dy = MathF.Max(MathF.Max(bounds.Minimum.Y - point.Y, 0.0f), point.Y - bounds.Maximum.Y);
        float dz = MathF.Max(MathF.Max(bounds.Minimum.Z - point.Z, 0.0f), point.Z - bounds.Maximum.Z);
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static BoundingBox ComputeWorldBounds(Matrix terrainWorldMatrix, TerrainComponent component, int originSampleX, int originSampleY, int sizeInSamples, float minHeight, float maxHeight)
    {
        int endSampleX = Math.Min(originSampleX + sizeInSamples, component.HeightmapWidth - 1);
        int endSampleY = Math.Min(originSampleY + sizeInSamples, component.HeightmapHeight - 1);
        float worldHeightScale = component.HeightScale * TerrainComponent.HeightSampleNormalization;

        Vector3[] corners =
        {
            new(originSampleX, minHeight * worldHeightScale, originSampleY),
            new(endSampleX, minHeight * worldHeightScale, originSampleY),
            new(originSampleX, minHeight * worldHeightScale, endSampleY),
            new(endSampleX, minHeight * worldHeightScale, endSampleY),
            new(originSampleX, maxHeight * worldHeightScale, originSampleY),
            new(endSampleX, maxHeight * worldHeightScale, originSampleY),
            new(originSampleX, maxHeight * worldHeightScale, endSampleY),
            new(endSampleX, maxHeight * worldHeightScale, endSampleY),
        };

        var worldMin = new Vector3(float.MaxValue);
        var worldMax = new Vector3(float.MinValue);
        foreach (var corner in corners)
        {
            var world = Vector3.TransformCoordinate(corner, terrainWorldMatrix);
            worldMin = Vector3.Min(worldMin, world);
            worldMax = Vector3.Max(worldMax, world);
        }

        return new BoundingBox(worldMin, worldMax);
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
