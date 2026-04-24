#nullable enable

using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace Terrain.Editor.Rendering.NativeViewport;

public sealed class PresenterViewportSceneRenderer : SceneRendererBase
{
    public ISceneRenderer? Child { get; set; }

    protected override void CollectCore(RenderContext context)
    {
        base.CollectCore(context);

        Texture? backBuffer = context.GraphicsDevice.Presenter?.BackBuffer;
        if (backBuffer == null)
        {
            return;
        }

        using (context.SaveRenderOutputAndRestore())
        using (context.SaveViewportAndRestore())
        {
            context.RenderOutput.RenderTargetFormat0 = backBuffer.ViewFormat;
            context.RenderOutput.RenderTargetCount = 1;
            context.ViewportState.Viewport0 = new Viewport(0, 0, backBuffer.ViewWidth, backBuffer.ViewHeight);
            Child?.Collect(context);
        }
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        Texture? backBuffer = drawContext.GraphicsDevice.Presenter?.BackBuffer;
        Texture? depthBuffer = drawContext.GraphicsDevice.Presenter?.DepthStencilBuffer;
        if (backBuffer == null || depthBuffer == null)
        {
            return;
        }

        using (drawContext.PushRenderTargetsAndRestore())
        {
            drawContext.CommandList.SetRenderTargetAndViewport(depthBuffer, backBuffer);
            Child?.Draw(drawContext);
        }
    }
}
