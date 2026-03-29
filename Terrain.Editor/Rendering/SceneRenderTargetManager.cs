#nullable enable
using Stride.Core.Mathematics;
using Stride.Graphics;
using System;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Manages scene render target creation and resizing.
/// Handles synchronization between ImGui panel size and Stride render target dimensions.
/// </summary>
public sealed class SceneRenderTargetManager : IDisposable
{
    private Texture? renderTarget;
    private Texture? depthBuffer;
    private Vector2 lastSize;

    /// <summary>
    /// Current render target for scene rendering.
    /// </summary>
    public Texture? RenderTarget => renderTarget;

    /// <summary>
    /// Current depth buffer for scene rendering.
    /// </summary>
    public Texture? DepthBuffer => depthBuffer;

    /// <summary>
    /// Gets or creates the render target with the specified size.
    /// Recreates the render target if size differs significantly.
    /// </summary>
    /// <param name="device">Graphics device for resource creation</param>
    /// <param name="size">Required render target size</param>
    /// <returns>The render target texture</returns>
    public Texture? GetOrCreate(GraphicsDevice device, Vector2 size)
    {
        if (size.X < 1 || size.Y < 1)
            return renderTarget;

        // Check if resize is needed (with 1 pixel tolerance for float precision)
        bool needsResize = renderTarget == null ||
                           MathF.Abs(renderTarget.Width - size.X) > 1 ||
                           MathF.Abs(renderTarget.Height - size.Y) > 1;

        if (needsResize)
        {
            // Dispose old resources
            renderTarget?.Dispose();
            depthBuffer?.Dispose();

            int width = Math.Max(1, (int)MathF.Round(size.X));
            int height = Math.Max(1, (int)MathF.Round(size.Y));

            // Create new render target
            renderTarget = Texture.New2D(
                device,
                width,
                height,
                PixelFormat.R8G8B8A8_UNorm_SRgb,
                TextureFlags.RenderTarget | TextureFlags.ShaderResource);

            // Create depth buffer
            depthBuffer = Texture.New2D(
                device,
                width,
                height,
                PixelFormat.D24_UNorm_S8_UInt,
                TextureFlags.DepthStencil);

            lastSize = new Vector2(width, height);
        }

        return renderTarget;
    }

    /// <summary>
    /// Gets the texture for use with ImGui rendering.
    /// The texture must be rendered through Stride's pipeline and displayed
    /// using a custom ImGui integration approach.
    /// </summary>
    /// <returns>The render target texture, or null if not created</returns>
    public Texture? GetTexture()
    {
        return renderTarget;
    }

    /// <summary>
    /// Invalidates the current render target, forcing recreation on next GetOrCreate.
    /// </summary>
    public void Invalidate()
    {
        renderTarget?.Dispose();
        depthBuffer?.Dispose();
        renderTarget = null;
        depthBuffer = null;
    }

    public void Dispose()
    {
        renderTarget?.Dispose();
        depthBuffer?.Dispose();
    }
}
