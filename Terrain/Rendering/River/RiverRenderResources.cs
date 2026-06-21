#nullable enable

using System;
using Stride.Graphics;

namespace Terrain.Rendering.River;

public sealed class RiverRenderResources : IDisposable
{
    public const PixelFormat RefractionFormat = PixelFormat.R16G16B16A16_Float;
    public const PixelFormat BottomDepthFormat = PixelFormat.D24_UNorm_S8_UInt;

    public Texture? SceneSeedColor { get; private set; }
    public Texture? BottomColor { get; private set; }
    public Texture? BottomDepth { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public void EnsureResources(GraphicsDevice graphicsDevice, int viewWidth, int viewHeight)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        var (width, height) = ComputeHalfResolutionSize(viewWidth, viewHeight);
        if (SceneSeedColor != null
            && BottomColor != null
            && BottomDepth != null
            && Width == width
            && Height == height)
        {
            return;
        }

        ReleaseResources();

        Width = width;
        Height = height;
        SceneSeedColor = Texture.New2D(
            graphicsDevice,
            width,
            height,
            RefractionFormat,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
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
        SceneSeedColor?.Dispose();
        SceneSeedColor = null;

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
