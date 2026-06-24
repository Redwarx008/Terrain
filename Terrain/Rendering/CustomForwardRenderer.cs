#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Storage;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Images;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using Terrain.Rendering.Ocean;
using Terrain.Rendering.River;
using Terrain.Rendering.Water;

namespace Terrain.Rendering;

[Display("Forward renderer (custom)")]
public partial class CustomForwardRenderer : SceneRendererBase, ISharedRenderer
{
    private static readonly ProfilingKey CollectCoreKey = new("ForwardRenderer.CollectCore");
    private static readonly ProfilingKey DrawCoreKey = new("ForwardRenderer.DrawCore");

    public const PixelFormat DepthBufferFormat = PixelFormat.D24_UNorm_S8_UInt;

    private IShadowMapRenderer? shadowMapRenderer;
    private Texture? depthStencilROCached;

    private readonly Logger logger = GlobalLogger.GetLogger(nameof(CustomForwardRenderer));

    private readonly List<Texture?> currentRenderTargets = [];
    private Texture? currentDepthStencil;

    private OceanRenderFeature? oceanRenderFeature;
    private RiverRenderFeature? riverRenderFeature;
    private WaterRefractionCapturePass? waterRefractionCapturePass;
    private readonly List<(RenderViewStage Stage, int StartIndex, int EndIndex)> oceanWaterRanges = [];
    private readonly List<(RenderViewStage Stage, int StartIndex, int EndIndex)> riverWaterRanges = [];

    protected Texture? viewOutputTarget;
    protected Texture? viewDepthStencil;

    public ClearRenderer Clear { get; set; } = new();

    public required RenderStage OpaqueRenderStage { get; set; }

    public required RenderStage TransparentRenderStage { get; set; }

    public RenderStage? WaterRenderStage { get; set; }

    [MemberCollection(NotNullItems = true)]
    public List<RenderStage> ShadowMapRenderStages { get; } = [];

    public required RenderStage GBufferRenderStage { get; set; }

    public IPostProcessingEffects? PostEffects { get; set; }

    [DefaultValue(true)]
    public bool BindDepthAsResourceDuringTransparentRendering { get; set; } = true;

    [DefaultValue(true)]
    public bool BindOpaqueAsResourceDuringTransparentRendering { get; set; } = true;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        shadowMapRenderer = Context.RenderSystem.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault()?.RenderFeatures.OfType<ForwardLightingRenderFeature>().FirstOrDefault()?.ShadowMapRenderer;
        oceanRenderFeature = Context.RenderSystem.RenderFeatures.OfType<OceanRenderFeature>().FirstOrDefault();
        riverRenderFeature = Context.RenderSystem.RenderFeatures.OfType<RiverRenderFeature>().FirstOrDefault();
        waterRefractionCapturePass = new WaterRefractionCapturePass(Context);
    }

    protected virtual void CollectStages(RenderContext context)
    {
        if (OpaqueRenderStage != null)
        {
            OpaqueRenderStage.OutputValidator.BeginCustomValidation(context.RenderOutput.DepthStencilFormat, context.RenderOutput.MultisampleCount);
            ValidateOpaqueStageOutput(OpaqueRenderStage.OutputValidator, context);
            OpaqueRenderStage.OutputValidator.EndCustomValidation();
        }

        if (TransparentRenderStage != null)
        {
            TransparentRenderStage.OutputValidator.Validate(ref context.RenderOutput);
        }

        if (WaterRenderStage != null)
        {
            WaterRenderStage.OutputValidator.Validate(ref context.RenderOutput);
        }

        if (GBufferRenderStage != null)
        {
            GBufferRenderStage.Output = new(PixelFormat.None, context.RenderOutput.DepthStencilFormat);
        }
    }

    protected virtual void ValidateOpaqueStageOutput(RenderOutputValidator renderOutputValidator, RenderContext renderContext)
    {
        renderOutputValidator.Add<ColorTargetSemantic>(renderContext.RenderOutput.RenderTargetFormat0);

        if (PostEffects != null)
        {
            if (PostEffects.RequiresNormalBuffer)
            {
                renderOutputValidator.Add<NormalTargetSemantic>(Platform.Type == PlatformType.Android || Platform.Type == PlatformType.iOS
                    ? PixelFormat.R16G16B16A16_Float
                    : PixelFormat.R10G10B10A2_UNorm);
            }

            if (PostEffects.RequiresSpecularRoughnessBuffer)
            {
                renderOutputValidator.Add<SpecularColorRoughnessTargetSemantic>(PixelFormat.R8G8B8A8_UNorm);
            }

            if (PostEffects.RequiresVelocityBuffer)
            {
                renderOutputValidator.Add<VelocityTargetSemantic>(PixelFormat.R16G16_Float);
            }
        }
    }

    protected virtual void CollectView(RenderContext context)
    {
        if (OpaqueRenderStage != null)
        {
            context.RenderView.RenderStages.Add(OpaqueRenderStage);
        }

        if (TransparentRenderStage != null)
        {
            context.RenderView.RenderStages.Add(TransparentRenderStage);
        }

        if (WaterRenderStage != null)
        {
            context.RenderView.RenderStages.Add(WaterRenderStage);
        }

        if (GBufferRenderStage != null)
        {
            context.RenderView.RenderStages.Add(GBufferRenderStage);
        }
    }

    protected override unsafe void CollectCore(RenderContext context)
    {
        using var _ = Profiler.Begin(CollectCoreKey);

        var camera = context.GetCurrentCamera();

        if (context.RenderView == null)
            throw new NullReferenceException(nameof(context.RenderView) + " is null. Please make sure you have your camera correctly set.");

        using (context.SaveRenderOutputAndRestore())
        {
            shadowMapRenderer?.RenderViewsWithShadows.Add(context.RenderView);

            context.RenderOutput = new(PostEffects != null ? PixelFormat.R16G16B16A16_Float : context.RenderOutput.RenderTargetFormat0, DepthBufferFormat);

            CollectStages(context);

            SceneCameraRenderer.UpdateCameraToRenderView(context, context.RenderView, camera);

            CollectView(context);

            PostEffects?.Collect(context);

            foreach (var shadowMapRenderStage in ShadowMapRenderStages)
            {
                if (shadowMapRenderStage != null)
                    shadowMapRenderStage.Output = new(PixelFormat.None, PixelFormat.D32_Float);
            }
        }

        PostEffects?.Collect(context);
    }

    protected virtual void DrawView(RenderContext context, RenderDrawContext drawContext, int eyeIndex, int eyeCount)
    {
        var renderSystem = context.RenderSystem;

        PrepareVRConstantBuffer(context, eyeIndex, eyeCount);

        if (GBufferRenderStage != null)
        {
            using (drawContext.QueryManager.BeginProfile(Color.Green, CompositingProfilingKeys.GBuffer))
            using (drawContext.PushRenderTargetsAndRestore())
            {
                drawContext.CommandList.Clear(drawContext.CommandList.DepthStencilBuffer, DepthStencilClearOptions.DepthBuffer);
                drawContext.CommandList.SetRenderTarget(drawContext.CommandList.DepthStencilBuffer, null);

                renderSystem.Draw(drawContext, context.RenderView, GBufferRenderStage);
            }
        }

        using (drawContext.PushRenderTargetsAndRestore())
        {
            if (OpaqueRenderStage != null)
            {
                using (drawContext.QueryManager.BeginProfile(Color.Green, CompositingProfilingKeys.Opaque))
                {
                    renderSystem.Draw(drawContext, context.RenderView, OpaqueRenderStage);
                }
            }

            var waterCapture = DrawWaterRefractionCapture(context, drawContext);
            DrawOceanWater(context, drawContext, waterCapture);
            DrawRiverWaterChain(context, drawContext, waterCapture);

            Texture? depthStencilSRV = null;

            if (TransparentRenderStage != null)
            {
                using (drawContext.QueryManager.BeginProfile(Color.Green, CompositingProfilingKeys.Transparent))
                using (drawContext.PushRenderTargetsAndRestore())
                {
                    if (depthStencilSRV == null)
                        depthStencilSRV = ResolveDepthAsSRV(drawContext);

                    var renderTargetSRV = ResolveRenderTargetAsSRV(drawContext);

                    renderSystem.Draw(drawContext, context.RenderView, TransparentRenderStage);

                    Context.Allocator.ReleaseReference(renderTargetSRV);
                }
            }

            var colorTargetIndex = OpaqueRenderStage?.OutputValidator.Find(typeof(ColorTargetSemantic)) ?? -1;
            if (colorTargetIndex == -1)
                return;

            var renderTargets = currentRenderTargets;
            var depthStencil = currentDepthStencil;

            PostEffects?.Draw(drawContext, OpaqueRenderStage!.OutputValidator, CollectionsMarshal.AsSpan(renderTargets), depthStencil, viewOutputTarget);

            if (depthStencilSRV != null)
            {
                drawContext.Resolver.ReleaseDepthStenctilAsShaderResource(depthStencilSRV);
            }
        }
    }

    protected virtual WaterRefractionCaptureResult? DrawWaterRefractionCapture(RenderContext context, RenderDrawContext drawContext)
    {
        oceanWaterRanges.Clear();
        riverWaterRanges.Clear();

        if (waterRefractionCapturePass == null
            || WaterRenderStage == null
            || context.RenderView == null)
        {
            return null;
        }

        Texture? sceneColor = drawContext.CommandList.RenderTargetCount > 0
            ? drawContext.CommandList.RenderTargets[0]
            : null;
        Texture? sceneDepth = drawContext.CommandList.DepthStencilBuffer ?? currentDepthStencil;
        if (sceneColor == null || sceneDepth == null)
        {
            return null;
        }

        CollectOceanWaterRangesFromWaterStage(context.RenderView, oceanWaterRanges);
        CollectRiverWaterRangesFromWaterStage(context.RenderView, riverWaterRanges);
        if (oceanWaterRanges.Count == 0 && riverWaterRanges.Count == 0)
        {
            return null;
        }

        Matrix.Invert(ref context.RenderView.View, out var viewInverse);
        float cameraWorldY = viewInverse.TranslationVector.Y;
        bool hasVisibleRiverWater = false;
        if (riverRenderFeature != null && riverWaterRanges.Count > 0)
        {
            float riverMaxVisibleCameraHeight = float.NegativeInfinity;
            foreach (var range in riverWaterRanges)
            {
                riverMaxVisibleCameraHeight = MathF.Max(
                    riverMaxVisibleCameraHeight,
                    riverRenderFeature.GetRiverMaxVisibleCameraHeight(range.Stage, range.StartIndex, range.EndIndex));
            }

            if (cameraWorldY >= riverMaxVisibleCameraHeight)
            {
                hasVisibleRiverWater = false;
            }
            else
            {
                hasVisibleRiverWater = true;
            }
        }

        if (oceanWaterRanges.Count == 0 && !hasVisibleRiverWater)
        {
            return null;
        }

        float refractionMaxCameraHeight = ResolveWaterRefractionMaxCameraHeight();
        return waterRefractionCapturePass.Capture(
            drawContext,
            context.RenderView,
            sceneColor,
            sceneDepth,
            refractionMaxCameraHeight);
    }

    protected virtual void DrawOceanWater(RenderContext context, RenderDrawContext drawContext, WaterRefractionCaptureResult? waterCapture)
    {
        if (oceanRenderFeature == null
            || context.RenderView == null
            || waterCapture == null
            || oceanWaterRanges.Count == 0)
        {
            return;
        }

        var capture = waterCapture.Value;
        float refractionMaxCameraHeight = capture.RefractionMaxCameraHeight;
        foreach (var range in oceanWaterRanges)
        {
            oceanRenderFeature.DrawWater(
                drawContext,
                context.RenderView,
                range.Stage,
                range.StartIndex,
                range.EndIndex,
                capture.Texture,
                capture.Width,
                capture.Height,
                refractionMaxCameraHeight);
        }
    }

    protected virtual void DrawRiverWaterChain(RenderContext context, RenderDrawContext drawContext, WaterRefractionCaptureResult? waterCapture)
    {
        if (riverRenderFeature == null
            || context.RenderView == null
            || waterCapture == null
            || riverWaterRanges.Count == 0)
        {
            return;
        }

        var capture = waterCapture.Value;
        float refractionMaxCameraHeight = capture.RefractionMaxCameraHeight;
        foreach (var range in riverWaterRanges)
        {
            riverRenderFeature.DrawWaterChain(
                drawContext,
                context.RenderView,
                range.Stage,
                range.StartIndex,
                range.EndIndex,
                capture.Texture,
                capture.Width,
                capture.Height,
                refractionMaxCameraHeight);
        }
    }

    private float ResolveWaterRefractionMaxCameraHeight()
    {
        float refractionMaxCameraHeight = 50.0f;
        if (riverRenderFeature == null)
        {
            return refractionMaxCameraHeight;
        }

        foreach (var range in riverWaterRanges)
        {
            refractionMaxCameraHeight = MathF.Max(
                refractionMaxCameraHeight,
                riverRenderFeature.GetRefractionMaxCameraHeight(range.Stage, range.StartIndex, range.EndIndex));
        }

        return refractionMaxCameraHeight;
    }

    private void CollectOceanWaterRangesFromWaterStage(
        RenderView renderView,
        List<(RenderViewStage Stage, int StartIndex, int EndIndex)> ranges)
    {
        var waterViewStage = FindRenderViewStage(renderView, WaterRenderStage!);
        if (waterViewStage.Index < 0 || waterViewStage.SortedRenderNodes == null)
        {
            return;
        }

        var renderNodes = waterViewStage.SortedRenderNodes;
        int rangeStart = -1;
        for (int index = 0; index < renderNodes.Count; index++)
        {
            bool isOceanNode = ReferenceEquals(renderNodes[index].RootRenderFeature, oceanRenderFeature)
                && renderNodes[index].RenderObject is OceanRenderObject;
            if (isOceanNode)
            {
                if (rangeStart < 0)
                {
                    rangeStart = index;
                }

                continue;
            }

            if (rangeStart >= 0)
            {
                ranges.Add((waterViewStage, rangeStart, index));
                rangeStart = -1;
            }
        }

        if (rangeStart >= 0)
        {
            ranges.Add((waterViewStage, rangeStart, renderNodes.Count));
        }
    }

    private void CollectRiverWaterRangesFromWaterStage(
        RenderView renderView,
        List<(RenderViewStage Stage, int StartIndex, int EndIndex)> ranges)
    {
        var waterViewStage = FindRenderViewStage(renderView, WaterRenderStage!);
        if (waterViewStage.Index < 0 || waterViewStage.SortedRenderNodes == null)
        {
            return;
        }

        var renderNodes = waterViewStage.SortedRenderNodes;
        int rangeStart = -1;
        for (int index = 0; index < renderNodes.Count; index++)
        {
            bool isRiverNode = ReferenceEquals(renderNodes[index].RootRenderFeature, riverRenderFeature)
                && renderNodes[index].RenderObject is RiverRenderObject;
            if (isRiverNode)
            {
                if (rangeStart < 0)
                {
                    rangeStart = index;
                }

                continue;
            }

            if (rangeStart >= 0)
            {
                ranges.Add((waterViewStage, rangeStart, index));
                rangeStart = -1;
            }
        }

        if (rangeStart >= 0)
        {
            ranges.Add((waterViewStage, rangeStart, renderNodes.Count));
        }
    }

    private static RenderViewStage FindRenderViewStage(RenderView renderView, RenderStage renderStage)
    {
        foreach (var renderViewStage in renderView.RenderStages)
        {
            if (renderViewStage.Index == renderStage.Index)
            {
                return renderViewStage;
            }
        }

        return RenderViewStage.Invalid;
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        using var _ = Profiler.Begin(DrawCoreKey);

        var viewport = drawContext.CommandList.Viewport;

        using (drawContext.PushRenderTargetsAndRestore())
        {
            shadowMapRenderer?.Draw(drawContext);

            PrepareRenderTargets(drawContext, new Size2((int)viewport.Width, (int)viewport.Height));

            using (drawContext.PushRenderTargetsAndRestore())
            {
                drawContext.CommandList.SetRenderTargets(currentDepthStencil, CollectionsMarshal.AsSpan(currentRenderTargets));

                Clear?.Draw(drawContext);

                DrawView(context, drawContext, 0, 1);
            }
        }

        currentRenderTargets.Clear();
        currentDepthStencil = null;
    }

    private Texture? ResolveDepthAsSRV(RenderDrawContext context)
    {
        if (!BindDepthAsResourceDuringTransparentRendering)
            return null;

        var depthStencil = context.CommandList.DepthStencilBuffer;
        var depthStencilSRV = context.Resolver.ResolveDepthStencil(context.CommandList.DepthStencilBuffer);

        var renderView = context.RenderContext.RenderView;

        foreach (var renderFeature in context.RenderContext.RenderSystem.RenderFeatures)
        {
            if (renderFeature is RootRenderFeature rootRenderFeature)
            {
                rootRenderFeature.BindPerViewShaderResource("Depth", renderView, depthStencilSRV);
            }
        }

        context.CommandList.SetRenderTargets(null, context.CommandList.RenderTargets);

        var depthStencilROCached = context.Resolver.GetDepthStencilAsRenderTarget(depthStencil, this.depthStencilROCached);
        if (depthStencilROCached != this.depthStencilROCached)
        {
            this.depthStencilROCached?.Dispose();
            this.depthStencilROCached = depthStencilROCached;
        }

        context.CommandList.SetRenderTargets(depthStencilROCached, context.CommandList.RenderTargets);

        return depthStencilSRV;
    }

    private Texture? ResolveRenderTargetAsSRV(RenderDrawContext drawContext)
    {
        if (!BindOpaqueAsResourceDuringTransparentRendering)
            return null;

        var renderTarget = drawContext.CommandList.RenderTargets[0];
        var renderTargetTexture = Context.Allocator.GetTemporaryTexture2D(renderTarget.Description);

        drawContext.CommandList.Copy(renderTarget, renderTargetTexture);

        var renderView = drawContext.RenderContext.RenderView;
        foreach (var renderFeature in drawContext.RenderContext.RenderSystem.RenderFeatures)
        {
            if (renderFeature is RootRenderFeature rootRenderFeature)
            {
                rootRenderFeature.BindPerViewShaderResource("Opaque", renderView, renderTargetTexture);
            }
        }

        return renderTargetTexture;
    }

    private void PrepareRenderTargets(RenderDrawContext drawContext, Texture outputRenderTarget, Texture outputDepthStencil)
    {
        if (OpaqueRenderStage == null)
            return;

        var renderTargets = OpaqueRenderStage.OutputValidator.RenderTargets;

        if (currentRenderTargets.Count < renderTargets.Count)
        {
            currentRenderTargets.EnsureCapacity(renderTargets.Count);
            while (currentRenderTargets.Count != renderTargets.Count)
                currentRenderTargets.Add(null);
        }
        else if (currentRenderTargets.Count > renderTargets.Count)
        {
            currentRenderTargets.RemoveRange(renderTargets.Count, currentRenderTargets.Count - renderTargets.Count);
        }

        for (int index = 0; index < renderTargets.Count; index++)
        {
            if (renderTargets[index].Semantic is ColorTargetSemantic && PostEffects == null)
            {
                currentRenderTargets[index] = outputRenderTarget;
            }
            else
            {
                var description = renderTargets[index];
                var textureDescription = TextureDescription.New2D(outputRenderTarget.Width, outputRenderTarget.Height, 1, description.Format, TextureFlags.RenderTarget | TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Default);
                currentRenderTargets[index] = PushScopedResource(drawContext.GraphicsContext.Allocator.GetTemporaryTexture2D(textureDescription));
            }

            drawContext.CommandList.ResourceBarrierTransition(currentRenderTargets[index], GraphicsResourceState.RenderTarget);
        }

        currentDepthStencil = outputDepthStencil;
        drawContext.CommandList.ResourceBarrierTransition(currentDepthStencil, GraphicsResourceState.DepthWrite);
    }

    protected virtual void PrepareRenderTargets(RenderDrawContext drawContext, Size2 renderTargetsSize)
    {
        viewOutputTarget = drawContext.CommandList.RenderTarget;
        if (drawContext.CommandList.RenderTargetCount == 0)
            viewOutputTarget = null;
        viewDepthStencil = drawContext.CommandList.DepthStencilBuffer;

        if ((viewOutputTarget != null && viewOutputTarget.MultisampleCount != MultisampleCount.None)
            || (viewDepthStencil != null && viewDepthStencil.MultisampleCount != MultisampleCount.None))
        {
            throw new InvalidOperationException("CustomForwardRenderer does not support MSAA output targets. Disable MSAA for this compositor or implement the full MSAA resolve/copy-back path before enabling it.");
        }

        if (viewOutputTarget == null || viewOutputTarget.MultisampleCount != MultisampleCount.None)
        {
            viewOutputTarget = PushScopedResource(drawContext.GraphicsContext.Allocator.GetTemporaryTexture2D(
                TextureDescription.New2D(renderTargetsSize.Width, renderTargetsSize.Height, 1, PixelFormat.R8G8B8A8_UNorm_SRgb,
                    TextureFlags.ShaderResource | TextureFlags.RenderTarget)));
        }

        if (viewDepthStencil == null || viewDepthStencil.MultisampleCount != MultisampleCount.None)
        {
            viewDepthStencil = PushScopedResource(drawContext.GraphicsContext.Allocator.GetTemporaryTexture2D(
                TextureDescription.New2D(renderTargetsSize.Width, renderTargetsSize.Height, 1, DepthBufferFormat,
                    TextureFlags.ShaderResource | TextureFlags.DepthStencil)));
        }

        PrepareRenderTargets(drawContext, viewOutputTarget, viewDepthStencil);
    }

    protected override void Destroy()
    {
        waterRefractionCapturePass?.Dispose();
        waterRefractionCapturePass = null;
        PostEffects?.Dispose();
        depthStencilROCached?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerViewVR
    {
        public int EyeIndex;
        public int EyeCount;
    }

    private unsafe void PrepareVRConstantBuffer(RenderContext context, int eyeIndex, int eyeCount)
    {
        foreach (var renderFeature in context.RenderSystem.RenderFeatures)
        {
            if (renderFeature is not RootEffectRenderFeature rootEffectRenderFeature)
                continue;

            var renderView = context.RenderView;
            var logicalKey = rootEffectRenderFeature.CreateViewLogicalGroup("GlobalVR");
            var viewFeature = renderView.Features[renderFeature.Index];

            foreach (var viewLayout in viewFeature.Layouts)
            {
                var resourceGroup = viewLayout.Entries[renderView.Index].Resources;

                var logicalGroup = viewLayout.GetLogicalGroup(logicalKey);
                if (logicalGroup.Hash == ObjectId.Empty)
                    continue;

                var mappedCB = (PerViewVR*)(resourceGroup.ConstantBuffer.Data + logicalGroup.ConstantBufferOffset);
                mappedCB->EyeIndex = eyeIndex;
                mappedCB->EyeCount = eyeCount;
            }
        }
    }
}
