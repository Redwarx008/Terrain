#nullable enable

using System;
using Stride.Graphics;

namespace Terrain.Rendering.River;

public sealed class RiverRenderResources : IDisposable
{
    public const PixelFormat RefractionFormat = PixelFormat.R16G16B16A16_Float;
    public const PixelFormat BottomDepthFormat = PixelFormat.D24_UNorm_S8_UInt;

    public Texture? BottomColor { get; private set; }
    public Texture? BottomDepth { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public void EnsureResources(GraphicsDevice graphicsDevice, int viewWidth, int viewHeight)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        var (width, height) = ComputeHalfResolutionSize(viewWidth, viewHeight);
        EnsureResourcesForCaptureSize(graphicsDevice, width, height);
    }

    public void EnsureResourcesForCaptureSize(GraphicsDevice graphicsDevice, int captureWidth, int captureHeight)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        int width = Math.Max(1, captureWidth);
        int height = Math.Max(1, captureHeight);
        if (BottomColor != null
            && BottomDepth != null
            && Width == width
            && Height == height)
        {
            return;
        }

        ReleaseResources();

        Width = width;
        Height = height;
        BottomColor = Texture.New2D(
            graphicsDevice,
            width,
            height,
            RefractionFormat,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        BottomDepth = Texture.New2D(
            graphicsDevice,
            width,
            height,
            BottomDepthFormat,
            TextureFlags.DepthStencil);
    }

    public void ReleaseResources()
    {
        BottomColor?.Dispose();
        BottomColor = null;

        BottomDepth?.Dispose();
        BottomDepth = null;

        Width = 0;
        Height = 0;
    }

    public void Dispose()
    {
        ReleaseResources();
    }

    public static (int Width, int Height) ComputeHalfResolutionSize(int viewWidth, int viewHeight)
    {
        int width = Math.Max(1, (viewWidth + 1) / 2);
        int height = Math.Max(1, (viewHeight + 1) / 2);
        return (width, height);
    }
}
