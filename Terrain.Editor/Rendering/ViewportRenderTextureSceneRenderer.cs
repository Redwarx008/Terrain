#nullable enable

using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Core.Mathematics;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Redirects the scene renderer chain into a dedicated render target so the editor viewport
/// can display the scene inside an ImGui panel instead of always drawing full-screen.
/// </summary>
public sealed class ViewportRenderTextureSceneRenderer : SceneRendererBase
{
    private static readonly Color4 RuntimeForwardClearColor = new(0.40491876f, 0.41189542f, 0.43775f, 1.0f);

    public Texture? RenderTexture { get; set; }

    public ISceneRenderer? Child { get; set; }

    public long DrawCount { get; private set; }

    protected override void CollectCore(RenderContext context)
    {
        base.CollectCore(context);

        if (RenderTexture == null)
        {
            return;
        }

        using (context.SaveRenderOutputAndRestore())
        using (context.SaveViewportAndRestore())
        {
            context.RenderOutput.RenderTargetFormat0 = RenderTexture.ViewFormat;
            context.ViewportState.Viewport0 = new Viewport(0, 0, RenderTexture.ViewWidth, RenderTexture.ViewHeight);
            Child?.Collect(context);
        }
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        if (RenderTexture == null)
        {
            return;
        }

        using (drawContext.PushRenderTargetsAndRestore())
        {
            var depthBuffer = PushScopedResource(
                context.Allocator.GetTemporaryTexture2D(
                    RenderTexture.ViewWidth,
                    RenderTexture.ViewHeight,
                    drawContext.CommandList.DepthStencilBuffer.ViewFormat,
                    TextureFlags.DepthStencil));
            drawContext.CommandList.SetRenderTargetAndViewport(depthBuffer, RenderTexture);

            // The runtime ForwardRenderer clears to a blue-gray color before drawing the sky/background.
            // Our offscreen path must do the same upfront; otherwise areas not covered by the background
            // chain stay black in the editor RT, which is exactly the mismatch visible versus Terrain.exe.
            drawContext.CommandList.Clear(RenderTexture, RuntimeForwardClearColor);
            drawContext.CommandList.Clear(depthBuffer, DepthStencilClearOptions.DepthBuffer);

            DrawCount++;
            Child?.Draw(drawContext);
        }
    }
}
