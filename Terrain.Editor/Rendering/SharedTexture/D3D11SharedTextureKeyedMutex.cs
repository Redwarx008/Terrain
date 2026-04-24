#nullable enable

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Silk.NET.DXGI;
using Silk.NET.Direct3D11;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.SharedTexture;

internal unsafe sealed class D3D11SharedTextureKeyedMutex : IDisposable
{
    private static readonly FieldInfo NativeResourceField =
        typeof(GraphicsResourceBase).GetField("nativeResource", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not locate Stride native resource field.");

    private const uint ProducerAcquireKey = 0;
    private const uint ProducerReleaseKey = 1;
    private const uint AcquireTimeoutMilliseconds = 0;
    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);

    private Silk.NET.Core.Native.ComPtr<IDXGIKeyedMutex> _keyedMutex;
    private bool _isDisposed;

    public D3D11SharedTextureKeyedMutex(Texture renderTarget)
    {
        ArgumentNullException.ThrowIfNull(renderTarget);

        object? boxedPointer = NativeResourceField.GetValue(renderTarget);
        if (boxedPointer == null)
        {
            throw new InvalidOperationException("Stride shared render target does not expose a native D3D11 resource.");
        }

        ID3D11Resource* nativeResource = (ID3D11Resource*)Pointer.Unbox(boxedPointer);
        if (nativeResource == null)
        {
            throw new InvalidOperationException("Stride shared render target native D3D11 resource pointer is null.");
        }

        int result = nativeResource->QueryInterface(out _keyedMutex);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public uint ConsumerAcquireKey => ProducerReleaseKey;

    public uint ConsumerReleaseKey => ProducerAcquireKey;

    public bool TryAcquireForWrite(out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        int result = _keyedMutex.AcquireSync(ProducerAcquireKey, AcquireTimeoutMilliseconds);
        if (result >= 0)
        {
            failureReason = null;
            return true;
        }

        failureReason = result == DxgiErrorWaitTimeout
            ? "Stride shared texture is waiting for Avalonia to release the keyed mutex; frame skipped."
            : $"Stride keyed mutex acquire failed: {result}.";
        return false;
    }

    public void ReleaseAfterWrite()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        int result = _keyedMutex.ReleaseSync(ProducerReleaseKey);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _keyedMutex.Dispose();
    }
}
