#nullable enable

using System;
using Stride.Graphics;
using Terrain.Rendering.River;

namespace Terrain.Rendering.Water;

public sealed class WaterRefractionCaptureResources : IDisposable
{
    public const PixelFormat RefractionFormat = PixelFormat.R16G16B16A16_Float;

    public Texture? RefractionTexture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public void EnsureResources(GraphicsDevice graphicsDevice, int viewWidth, int viewHeight)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        var (width, height) = RiverRenderResources.ComputeHalfResolutionSize(viewWidth, viewHeight);
        if (RefractionTexture != null && Width == width && Height == height)
        {
            return;
        }

        ReleaseResources();

        Width = width;
        Height = height;
        RefractionTexture = Texture.New2D(
            graphicsDevice,
            width,
            height,
            RefractionFormat,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
    }

    public void ReleaseResources()
    {
        RefractionTexture?.Dispose();
        RefractionTexture = null;
        Width = 0;
        Height = 0;
    }

    public void Dispose()
    {
        ReleaseResources();
    }
}
