#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.SharedTexture;

internal sealed class DiagnosticStrideOffscreenViewportRenderer : IStrideOffscreenViewportRenderer
{
    public string Description => "Diagnostic clear renderer";

    public bool Render(in StrideOffscreenViewportRenderContext context)
    {
        CommandList commandList = context.CommandList;
        Color4 clearColor = CreateDiagnosticClearColor(context);

        commandList.SetRenderTargetAndViewport(context.DepthBuffer, context.RenderTarget);
        commandList.Clear(context.RenderTarget, clearColor);
        commandList.Clear(context.DepthBuffer, DepthStencilClearOptions.DepthBuffer | DepthStencilClearOptions.Stencil);
        return true;
    }

    private static Color4 CreateDiagnosticClearColor(in StrideOffscreenViewportRenderContext context)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin((float)context.Elapsed.TotalSeconds * 1.75f);
        float frameStripe = (context.FrameNumber % 120) / 120.0f;

        return new Color4(
            0.08f + 0.10f * pulse,
            0.11f + 0.08f * frameStripe,
            0.15f + 0.12f * (1.0f - pulse),
            1.0f);
    }
}
