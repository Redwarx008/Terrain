#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Terrain.Editor.Rendering.SharedTexture;

namespace Terrain.Editor.Views.Controls;

public sealed class SharedTextureViewportControl : Control
{
    public static readonly StyledProperty<ISharedTextureViewportSource?> SourceProperty =
        AvaloniaProperty.Register<SharedTextureViewportControl, ISharedTextureViewportSource?>(nameof(Source));

    private static readonly Typeface StatusTypeface = new("Inter");
    private CompositionDrawingSurface? _compositionSurface;
    private CompositionSurfaceVisual? _compositionVisual;
    private ICompositionGpuInterop? _gpuInterop;
    private string _compositionStatus = "Avalonia Composition GPU interop not initialized.";
    private ulong _importedVersion;
    private bool _isInitializingComposition;
    private bool _isImportingFrame;
    private ISharedTextureViewportSource? _subscribedSource;

    public ISharedTextureViewportSource? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            RequestSourceResize();
            UpdateCompositionVisualSize();
        }

        if (change.Property == SourceProperty)
        {
            SubscribeToSource(change.NewValue as ISharedTextureViewportSource);
            RequestSourceResize();
            InvalidateVisual();
            QueueFrameImport();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        context.FillRectangle(Brushes.Black, bounds);

        string status = $"{Source?.Status ?? "Shared texture source unavailable."}{Environment.NewLine}{_compositionStatus}";
        var foreground = Source?.IsAvailable == true ? Brushes.White : Brushes.LightGray;
        var formattedText = new FormattedText(
            status,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            StatusTypeface,
            13,
            foreground);

        var origin = new Point(12, Math.Max(12, bounds.Height - formattedText.Height - 12));
        context.DrawText(formattedText, origin);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToSource(Source);
        _ = EnsureCompositionResourcesAsync();
        RequestSourceResize();
        QueueFrameImport();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToSource(null);

        _compositionSurface?.Dispose();
        _compositionSurface = null;
        _compositionVisual = null;
        _gpuInterop = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToSource(ISharedTextureViewportSource? source)
    {
        if (ReferenceEquals(_subscribedSource, source))
        {
            return;
        }

        if (_subscribedSource != null)
        {
            _subscribedSource.FrameChanged -= OnFrameChanged;
        }

        _subscribedSource = source;

        if (_subscribedSource != null)
        {
            _subscribedSource.FrameChanged += OnFrameChanged;
        }
    }

    private void OnFrameChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            QueueFrameImport();
            InvalidateVisual();
        });
    }

    private void RequestSourceResize()
    {
        if (Source == null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double scale = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        int pixelWidth = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
        int pixelHeight = Math.Max(1, (int)Math.Round(Bounds.Height * scale));
        Source.RequestResize(pixelWidth, pixelHeight, scale);
    }

    private async Task EnsureCompositionResourcesAsync()
    {
        if (_compositionSurface != null || _isInitializingComposition)
        {
            return;
        }

        _isInitializingComposition = true;
        try
        {
            var elementVisual = ElementComposition.GetElementVisual(this);
            if (elementVisual == null)
            {
                _compositionStatus = "Avalonia element composition visual is unavailable; GPU import disabled.";
                return;
            }

            var compositor = elementVisual.Compositor;
            _compositionSurface = compositor.CreateDrawingSurface();
            _compositionVisual = compositor.CreateSurfaceVisual();
            _compositionVisual.Surface = _compositionSurface;
            ElementComposition.SetElementChildVisual(this, _compositionVisual);
            UpdateCompositionVisualSize();

            _gpuInterop = await compositor.TryGetCompositionGpuInterop();
            if (_gpuInterop == null)
            {
                _compositionStatus = "Avalonia GPU interop unavailable; viewport will show diagnostics only.";
                return;
            }

            if (_gpuInterop.IsLost)
            {
                _compositionStatus = "Avalonia GPU interop device is lost; viewport import disabled.";
                return;
            }

            bool supportsNtHandle = _gpuInterop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle);
            _compositionStatus = supportsNtHandle
                ? "Avalonia GPU interop supports D3D11 NT texture handles; waiting for Stride frame."
                : "Avalonia GPU interop does not support D3D11 NT texture handles.";
        }
        catch (Exception exception)
        {
            _compositionStatus = $"Avalonia Composition GPU interop initialization failed: {exception.Message}";
        }
        finally
        {
            _isInitializingComposition = false;
            InvalidateVisual();
        }
    }

    private void QueueFrameImport()
    {
        if (_isImportingFrame)
        {
            return;
        }

        var frame = Source?.CurrentFrame;
        if (frame == null || frame.Value.SharedHandle == nint.Zero)
        {
            return;
        }

        _ = ImportFrameAsync(frame.Value);
    }

    private async Task ImportFrameAsync(SharedTextureFrame frame)
    {
        _isImportingFrame = true;
        try
        {
            await EnsureCompositionResourcesAsync();

            if (_compositionSurface == null || _gpuInterop == null)
            {
                return;
            }

            if (_gpuInterop.IsLost)
            {
                _compositionStatus = "Avalonia GPU interop device is lost; viewport import disabled.";
                return;
            }

            if (!_gpuInterop.SupportedImageHandleTypes.Contains(frame.HandleType))
            {
                _compositionStatus = $"Avalonia GPU interop does not support image handle type '{frame.HandleType}'.";
                return;
            }

            var syncCapabilities = _gpuInterop.GetSynchronizationCapabilities(frame.HandleType);
            bool supportsKeyedMutex =
                syncCapabilities.HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex);
            bool supportsAutomatic =
                syncCapabilities.HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.Automatic);

            if (frame.UsesKeyedMutex && !supportsKeyedMutex && !supportsAutomatic)
            {
                _compositionStatus = "Avalonia GPU interop does not expose a safe synchronization mode for this shared texture.";
                return;
            }

            var handle = new PlatformHandle(frame.SharedHandle, frame.HandleType);
            var properties = new PlatformGraphicsExternalImageProperties
            {
                Width = frame.Width,
                Height = frame.Height,
                Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
                MemorySize = checked((ulong)frame.Width * (ulong)frame.Height * 4UL),
                MemoryOffset = 0,
                TopLeftOrigin = true,
            };

            var importedImage = _gpuInterop.ImportImage(handle, properties);
            try
            {
                await importedImage.ImportCompleted;
                if (importedImage.IsLost)
                {
                    _compositionStatus = "Avalonia imported GPU image was lost before surface update.";
                    return;
                }

                if (frame.UsesKeyedMutex && supportsKeyedMutex)
                {
                    await _compositionSurface.UpdateWithKeyedMutexAsync(
                        importedImage,
                        frame.KeyedMutexAcquireKey,
                        frame.KeyedMutexReleaseKey);
                }
                else
                {
                    await _compositionSurface.UpdateAsync(importedImage);
                }

                _importedVersion = frame.Version;
                _compositionStatus = $"Avalonia imported shared texture version {_importedVersion}.";
            }
            finally
            {
                await importedImage.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            _compositionStatus = $"Avalonia shared texture import failed: {exception.Message}";
        }
        finally
        {
            _isImportingFrame = false;
            InvalidateVisual();
        }
    }

    private void UpdateCompositionVisualSize()
    {
        if (_compositionVisual == null)
        {
            return;
        }

        _compositionVisual.Size = new Vector(Math.Max(0, Bounds.Width), Math.Max(0, Bounds.Height));
    }
}
