#nullable enable

using System;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.SharedTexture;

/// <summary>
/// Renders one frame into the shared-texture viewport target.
/// Implementations must only render into the supplied offscreen target and must not create native-window-backed views.
/// </summary>
public interface IStrideOffscreenViewportRenderer
{
    string Description { get; }

    bool Render(in StrideOffscreenViewportRenderContext context);
}

public readonly struct StrideOffscreenViewportRenderContext
{
    public StrideOffscreenViewportRenderContext(
        GraphicsDevice graphicsDevice,
        GraphicsContext graphicsContext,
        Texture renderTarget,
        Texture depthBuffer,
        ulong frameNumber,
        TimeSpan elapsed)
    {
        GraphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        GraphicsContext = graphicsContext ?? throw new ArgumentNullException(nameof(graphicsContext));
        RenderTarget = renderTarget ?? throw new ArgumentNullException(nameof(renderTarget));
        DepthBuffer = depthBuffer ?? throw new ArgumentNullException(nameof(depthBuffer));
        FrameNumber = frameNumber;
        Elapsed = elapsed;
    }

    public GraphicsContext GraphicsContext { get; }

    public GraphicsDevice GraphicsDevice { get; }

    public CommandList CommandList => GraphicsContext.CommandList;

    public Texture RenderTarget { get; }

    public Texture DepthBuffer { get; }

    public ulong FrameNumber { get; }

    public TimeSpan Elapsed { get; }
}

public sealed class DelegateStrideOffscreenViewportRenderer : IStrideOffscreenViewportRenderer
{
    private readonly Func<StrideOffscreenViewportRenderContext, bool> _render;

    public DelegateStrideOffscreenViewportRenderer(
        string description,
        Func<StrideOffscreenViewportRenderContext, bool> render)
    {
        Description = string.IsNullOrWhiteSpace(description)
            ? "Delegate offscreen viewport renderer"
            : description;
        _render = render ?? throw new ArgumentNullException(nameof(render));
    }

    public string Description { get; }

    public bool Render(in StrideOffscreenViewportRenderContext context)
    {
        return _render(context);
    }
}
