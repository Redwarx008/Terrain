#nullable enable

using System;
using System.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Images;

namespace Terrain.Rendering.Water;

public sealed class WaterRefractionCapturePass : IDisposable
{
    private readonly WaterRefractionCaptureResources resources = new();
    private readonly ImageEffectShader effect = new ImageEffectShader("WaterRefractionCapture", delaySetRenderTargets: true);

    public WaterRefractionCapturePass(RenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(renderContext);

        effect.Initialize(renderContext);
    }

    public WaterRefractionCaptureResult Capture(
        RenderDrawContext context,
        RenderView renderView,
        Texture sceneColor,
        float refractionMaxCameraHeight)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderView);
        ArgumentNullException.ThrowIfNull(sceneColor);

        resources.EnsureResources(context.GraphicsDevice, sceneColor.ViewWidth, sceneColor.ViewHeight);
        Debug.Assert(resources.RefractionTexture != null, "Water refraction capture target has not been allocated.");
        Debug.Assert(!ReferenceEquals(sceneColor, resources.RefractionTexture), "Water refraction capture input and output must be different textures.");

        var captureTarget = resources.RefractionTexture!;
        var sceneDepthSource = GetPresenterSceneDepthSource(context.GraphicsDevice, sceneColor);
        var sceneDepth = context.Resolver.ResolveDepthStencil(sceneDepthSource);
        if (sceneDepth == null)
        {
            throw new InvalidOperationException("Water refraction capture requires a depth buffer that can be resolved as a shader resource.");
        }

        try
        {
            effect.Parameters.Set(DepthBaseKeys.DepthStencil, sceneDepth);
            effect.Parameters.Set(CameraKeys.ViewSize, new Vector2(sceneColor.ViewWidth, sceneColor.ViewHeight));
            effect.Parameters.Set(CameraKeys.ZProjection, CameraKeys.ZProjectionACalculate(
                renderView.NearClipPlane,
                renderView.FarClipPlane));
            effect.Parameters.Set(CameraKeys.NearClipPlane, renderView.NearClipPlane);
            effect.Parameters.Set(CameraKeys.FarClipPlane, renderView.FarClipPlane);
            var viewInverse = Matrix.Invert(renderView.View);
            effect.Parameters.Set(TransformationKeys.ViewInverse, ref viewInverse);
            effect.Parameters.Set(TransformationKeys.Eye, new Vector4(viewInverse.TranslationVector, 1.0f));
            effect.Parameters.Set(RiverCommonKeys._RefractionMaxCameraHeight, refractionMaxCameraHeight);
            effect.Parameters.Set(WaterRefractionCaptureKeys._RefractionCaptureExposure, 1.0f);
            effect.Parameters.Set(WaterRefractionCaptureKeys._RefractionCaptureColorScale, 1.5f);
            Matrix.Invert(ref renderView.Projection, out var projectionInverse);
            effect.Parameters.Set(TransformationKeys.ProjectionInverse, ref projectionInverse);
            effect.Parameters.Set(TexturingKeys.Sampler, context.GraphicsDevice.SamplerStates.LinearClamp);
            using (context.PushRenderTargetsAndRestore())
            {
                effect.SetInput(0, sceneColor);
                effect.SetOutput(captureTarget);
                effect.Draw(context, "Water refraction capture");
            }
        }
        finally
        {
            context.Resolver.ReleaseDepthStenctilAsShaderResource(sceneDepth);
        }

        return new WaterRefractionCaptureResult(captureTarget, resources.Width, resources.Height, refractionMaxCameraHeight);
    }

    public void ReleaseResources()
    {
        resources.ReleaseResources();
    }

    public void Dispose()
    {
        effect.Dispose();
        resources.Dispose();
    }

    private static Texture GetPresenterSceneDepthSource(GraphicsDevice graphicsDevice, Texture sceneColor)
    {
        var presenter = graphicsDevice.Presenter;
        if (presenter == null)
        {
            throw new InvalidOperationException("Water refraction capture requires GraphicsDevice.Presenter. Offscreen water rendering must provide an explicit scene-depth source before enabling this pass.");
        }

        var sceneDepth = presenter.DepthStencilBuffer;
        if (sceneDepth == null)
        {
            throw new InvalidOperationException("Water refraction capture requires GraphicsDevice.Presenter.DepthStencilBuffer.");
        }

        if (sceneDepth.ViewWidth != sceneColor.ViewWidth || sceneDepth.ViewHeight != sceneColor.ViewHeight)
        {
            throw new InvalidOperationException($"Water refraction capture depth size {sceneDepth.ViewWidth}x{sceneDepth.ViewHeight} must match scene color size {sceneColor.ViewWidth}x{sceneColor.ViewHeight}.");
        }

        return sceneDepth;
    }
}

public readonly record struct WaterRefractionCaptureResult(Texture Texture, int Width, int Height, float RefractionMaxCameraHeight);
