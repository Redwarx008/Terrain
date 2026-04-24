#nullable enable

using System;
using System.ComponentModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.SharedTexture;

public class StrideSharedTextureViewportSource : ObservableObject, ISharedTextureViewportSource, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly SynchronizationContext? _notificationContext = SynchronizationContext.Current;
    private readonly SharedTextureRenderTargetManager _renderTargets = new();
    private GraphicsDevice? _graphicsDevice;
    private string _baseStatus = "Viewport target not measured; waiting for Avalonia layout.";
    private string _status = "Viewport target not measured; waiting for Avalonia layout.";
    private string? _runtimeStatus;
    private bool _isAvailable;
    private int _pixelWidth;
    private int _pixelHeight;
    private double _dpiScale = 1.0;
    private string? _graphicsDeviceUnavailableMessage;
    private bool _isDisposed;

    public event EventHandler? FrameChanged;

    public SharedTextureFrame? CurrentFrame { get; private set; }

    public Texture? RenderTarget => _renderTargets.RenderTarget;

    public Texture? DepthBuffer => _renderTargets.DepthBuffer;

    public string Status
    {
        get => _status;
        private set => SetStatus(value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set => SetIsAvailable(value);
    }

    public void AttachGraphicsDevice(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        bool frameChanged;
        lock (_syncRoot)
        {
            if (ReferenceEquals(_graphicsDevice, graphicsDevice))
            {
                frameChanged = EnsureFrameLocked();
            }
            else
            {
                _graphicsDevice = graphicsDevice;
                _graphicsDeviceUnavailableMessage = null;
                _renderTargets.Invalidate();
                SetBaseStatusLocked("Stride GraphicsDevice attached; waiting for viewport size.");
                frameChanged = EnsureFrameLocked();
            }
        }

        RaiseFrameChangedIfNeeded(frameChanged);
    }

    public void DetachGraphicsDevice()
    {
        lock (_syncRoot)
        {
            _graphicsDevice = null;
            _graphicsDeviceUnavailableMessage = null;
            _renderTargets.Invalidate();
            PublishUnavailableLocked("Stride GraphicsDevice detached; shared texture is unavailable.");
        }

        RaiseFrameChanged();
    }

    public void SetGraphicsDeviceUnavailable(string message)
    {
        lock (_syncRoot)
        {
            _graphicsDevice = null;
            _graphicsDeviceUnavailableMessage = string.IsNullOrWhiteSpace(message)
                ? "Stride GraphicsDevice is unavailable; shared texture is unavailable."
                : message;
            _renderTargets.Invalidate();
            PublishUnavailableLocked(_graphicsDeviceUnavailableMessage);
        }

        RaiseFrameChanged();
    }

    public void RequestResize(int pixelWidth, int pixelHeight, double dpiScale)
    {
        bool frameChanged;
        lock (_syncRoot)
        {
            _pixelWidth = Math.Max(1, pixelWidth);
            _pixelHeight = Math.Max(1, pixelHeight);
            _dpiScale = Math.Max(0.25, dpiScale);
            frameChanged = EnsureFrameLocked();
        }

        RaiseFrameChangedIfNeeded(frameChanged);
    }

    internal void SetDiagnosticStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_syncRoot)
        {
            SetBaseStatusLocked(message);
        }
    }

    internal void SetRuntimeStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_syncRoot)
        {
            _runtimeStatus = message;
            ApplyCurrentStatusLocked();
            UpdateDiagnosticFrameMessageLocked();
        }

        RaiseFrameChanged();
    }

    public void PublishRenderedFrame()
    {
        bool frameChanged = false;
        lock (_syncRoot)
        {
            if (CurrentFrame.HasValue)
            {
                CurrentFrame = CurrentFrame.Value with { Version = CurrentFrame.Value.Version + 1 };
                frameChanged = true;
            }
        }

        RaiseFrameChangedIfNeeded(frameChanged);
    }

    internal bool TryRenderOffscreenFrame(
        GraphicsContext graphicsContext,
        IStrideOffscreenViewportRenderer renderer,
        ulong frameVersion,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(graphicsContext);
        ArgumentNullException.ThrowIfNull(renderer);

        lock (_syncRoot)
        {
            if (_isDisposed || _graphicsDevice == null || !CurrentFrame.HasValue || !IsAvailable)
            {
                return false;
            }

            Texture? renderTarget = _renderTargets.RenderTarget;
            Texture? depthBuffer = _renderTargets.DepthBuffer;
            if (renderTarget == null || depthBuffer == null)
            {
                return false;
            }

            try
            {
                bool acquiredWriteAccess = false;
                CommandList commandList = graphicsContext.CommandList;
                try
                {
                    if (CurrentFrame.Value.UsesKeyedMutex &&
                        !_renderTargets.TryAcquireWriteAccess(out string? keyedMutexStatus))
                    {
                        SetBaseStatusLocked(keyedMutexStatus ?? "Stride shared texture keyed mutex blocked the frame.");
                        return false;
                    }

                    acquiredWriteAccess = CurrentFrame.Value.UsesKeyedMutex;
                    commandList.Reset();
                    var renderContext = new StrideOffscreenViewportRenderContext(
                        _graphicsDevice,
                        graphicsContext,
                        renderTarget,
                        depthBuffer,
                        frameVersion,
                        elapsed);

                    if (!renderer.Render(renderContext))
                    {
                        SetBaseStatusLocked($"Stride offscreen renderer skipped frame: {renderer.Description}.");
                        commandList.ResetTargets();
                        return false;
                    }

                    commandList.ResetTargets();
                    commandList.Flush();
                    if (acquiredWriteAccess)
                    {
                        _renderTargets.ReleaseWriteAccess();
                        acquiredWriteAccess = false;
                    }
                }
                finally
                {
                    if (acquiredWriteAccess)
                    {
                        _renderTargets.ReleaseWriteAccess();
                    }
                }

                CurrentFrame = CurrentFrame.Value with { Version = frameVersion };
                SharedTextureFrame frame = CurrentFrame.Value;
                SetBaseStatusLocked(
                    $"Stride shared texture {frame.Width}x{frame.Height} @ {frame.DpiScale:0.##}x; renderer {renderer.Description}.");
            }
            catch (Exception exception)
            {
                _renderTargets.Invalidate();
                PublishUnavailableLocked($"Stride offscreen render loop failed:{Environment.NewLine}{exception}");
            }
        }

        RaiseFrameChanged();
        return true;
    }

    public virtual void Dispose()
    {
        lock (_syncRoot)
        {
            _isDisposed = true;
            _renderTargets.Dispose();
            CurrentFrame = null;
            IsAvailable = false;
        }
    }

    internal void PublishRenderLoopFault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _renderTargets.Invalidate();
            PublishUnavailableLocked($"Stride offscreen render loop stopped:{Environment.NewLine}{exception}");
        }

        RaiseFrameChanged();
    }

    private bool EnsureFrameLocked()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0)
        {
            PublishUnavailableLocked("Viewport target not measured; waiting for Avalonia layout.");
            return true;
        }

        if (_graphicsDevice == null)
        {
            string message = _graphicsDeviceUnavailableMessage ??
                $"Viewport target {_pixelWidth}x{_pixelHeight} @ {_dpiScale:0.##}x; waiting for Stride GraphicsDevice.";
            PublishUnavailableLocked(message);
            return true;
        }

        try
        {
            var frame = _renderTargets.GetOrCreate(_graphicsDevice, _pixelWidth, _pixelHeight, _dpiScale);
            CurrentFrame = frame;
            IsAvailable = true;
            SetBaseStatusLocked(
                $"Stride shared texture {frame.Width}x{frame.Height} @ {frame.DpiScale:0.##}x; handle type {frame.HandleType}.");
            return true;
        }
        catch (Exception exception)
        {
            _renderTargets.Invalidate();
            PublishUnavailableLocked($"Failed to create Stride shared texture: {exception.Message}");
            return true;
        }
    }

    private void PublishUnavailableLocked(string message)
    {
        _baseStatus = message;
        string composedStatus = ComposeStatus(message, _runtimeStatus);
        CurrentFrame = new SharedTextureFrame(
            nint.Zero,
            SharedTextureHandleTypes.D3D11TextureNtHandle,
            Math.Max(0, _pixelWidth),
            Math.Max(0, _pixelHeight),
            Math.Max(0.25, _dpiScale),
            0,
            SharedTextureRenderTargetManager.ColorFormat,
            UsesKeyedMutex: true,
            KeyedMutexAcquireKey: 0,
            KeyedMutexReleaseKey: 0,
            SharedNtHandleName: null,
            DiagnosticMessage: composedStatus);

        IsAvailable = false;
        Status = composedStatus;
    }

    private void RaiseFrameChangedIfNeeded(bool frameChanged)
    {
        if (frameChanged)
        {
            RaiseFrameChanged();
        }
    }

    private void RaiseFrameChanged()
    {
        EventHandler? handler = FrameChanged;
        if (handler == null)
        {
            return;
        }

        if (_notificationContext != null && SynchronizationContext.Current != _notificationContext)
        {
            _notificationContext.Post(static state =>
            {
                var (source, capturedHandler) = ((StrideSharedTextureViewportSource Source, EventHandler Handler))state!;
                capturedHandler(source, EventArgs.Empty);
            }, (this, handler));
            return;
        }

        handler(this, EventArgs.Empty);
    }

    private void SetStatus(string value)
    {
        if (_status == value)
        {
            return;
        }

        _status = value;
        RaisePropertyChangedOnNotificationContext(nameof(Status));
    }

    private void SetIsAvailable(bool value)
    {
        if (_isAvailable == value)
        {
            return;
        }

        _isAvailable = value;
        RaisePropertyChangedOnNotificationContext(nameof(IsAvailable));
    }

    private void RaisePropertyChangedOnNotificationContext(string propertyName)
    {
        if (_notificationContext != null && SynchronizationContext.Current != _notificationContext)
        {
            _notificationContext.Post(static state =>
            {
                var (source, capturedPropertyName) = ((StrideSharedTextureViewportSource Source, string PropertyName))state!;
                source.OnPropertyChanged(new PropertyChangedEventArgs(capturedPropertyName));
            }, (this, propertyName));
            return;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }

    private void SetBaseStatusLocked(string value)
    {
        _baseStatus = value;
        ApplyCurrentStatusLocked();
        UpdateDiagnosticFrameMessageLocked();
    }

    private void ApplyCurrentStatusLocked()
    {
        Status = ComposeStatus(_baseStatus, _runtimeStatus);
    }

    private void UpdateDiagnosticFrameMessageLocked()
    {
        if (!CurrentFrame.HasValue || CurrentFrame.Value.SharedHandle != nint.Zero)
        {
            return;
        }

        CurrentFrame = CurrentFrame.Value with
        {
            DiagnosticMessage = ComposeStatus(_baseStatus, _runtimeStatus),
        };
    }

    private static string ComposeStatus(string baseStatus, string? runtimeStatus)
    {
        if (string.IsNullOrWhiteSpace(runtimeStatus))
        {
            return baseStatus;
        }

        return $"{baseStatus}{Environment.NewLine}{runtimeStatus}";
    }
}
