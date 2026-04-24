#nullable enable

using System;
using System.Runtime.InteropServices;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.SharedTexture;

public sealed class SharedTextureRenderTargetManager : IDisposable
{
    public const string ColorFormat = "R8G8B8A8_UNorm";

    private Texture? _renderTarget;
    private Texture? _depthBuffer;
    private D3D11SharedTextureKeyedMutex? _keyedMutex;
    private int _width;
    private int _height;
    private ulong _version;

    public Texture? RenderTarget => _renderTarget;

    public Texture? DepthBuffer => _depthBuffer;

    public SharedTextureFrame GetOrCreate(GraphicsDevice graphicsDevice, int width, int height, double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        int targetWidth = Math.Max(1, width);
        int targetHeight = Math.Max(1, height);

        bool needsRecreate =
            _renderTarget == null ||
            _depthBuffer == null ||
            _width != targetWidth ||
            _height != targetHeight ||
            _renderTarget.SharedHandle == IntPtr.Zero;

        if (needsRecreate)
        {
            DisposeTargets();

            _renderTarget = Texture.New2D(
                graphicsDevice,
                targetWidth,
                targetHeight,
                PixelFormat.R8G8B8A8_UNorm,
                TextureFlags.RenderTarget | TextureFlags.ShaderResource,
                arraySize: 1,
                usage: GraphicsResourceUsage.Default,
                options: TextureOptions.SharedNtHandle | TextureOptions.SharedKeyedMutex);

            if (_renderTarget.SharedHandle == IntPtr.Zero)
            {
                DisposeTargets();
                throw new InvalidOperationException("Stride created a shared render target without a shared NT handle.");
            }

            _keyedMutex = new D3D11SharedTextureKeyedMutex(_renderTarget);

            _depthBuffer = Texture.New2D(
                graphicsDevice,
                targetWidth,
                targetHeight,
                PixelFormat.D24_UNorm_S8_UInt,
                TextureFlags.DepthStencil);

            _width = targetWidth;
            _height = targetHeight;
            _version++;
        }

        var renderTarget = _renderTarget ?? throw new InvalidOperationException("Shared render target was not created.");

        return new SharedTextureFrame(
            renderTarget.SharedHandle,
            SharedTextureHandleTypes.D3D11TextureNtHandle,
            _width,
            _height,
            Math.Max(0.25, dpiScale),
            _version,
            ColorFormat,
            UsesKeyedMutex: true,
            KeyedMutexAcquireKey: _keyedMutex?.ConsumerAcquireKey ?? 0,
            KeyedMutexReleaseKey: _keyedMutex?.ConsumerReleaseKey ?? 0,
            renderTarget.SharedNtHandleName,
            DiagnosticMessage: null);
    }

    public bool TryAcquireWriteAccess(out string? failureReason)
    {
        if (_keyedMutex == null)
        {
            failureReason = "Stride shared texture keyed mutex is unavailable.";
            return false;
        }

        return _keyedMutex.TryAcquireForWrite(out failureReason);
    }

    public void ReleaseWriteAccess()
    {
        _keyedMutex?.ReleaseAfterWrite();
    }

    public void Invalidate()
    {
        DisposeTargets();
        _width = 0;
        _height = 0;
        _version++;
    }

    public void Dispose()
    {
        DisposeTargets();
    }

    private void DisposeTargets()
    {
        _keyedMutex?.Dispose();
        _keyedMutex = null;
        CloseSharedNtHandle(_renderTarget);
        _renderTarget?.Dispose();
        _depthBuffer?.Dispose();
        _renderTarget = null;
        _depthBuffer = null;
    }

    private static void CloseSharedNtHandle(Texture? texture)
    {
        nint handle = texture?.SharedHandle ?? nint.Zero;
        if (handle != nint.Zero)
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
