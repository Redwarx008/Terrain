#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.SharedTexture;

public sealed class StrideOffscreenViewportRenderLoop : IDisposable
{
    private static readonly TimeSpan FrameDelay = TimeSpan.FromMilliseconds(16);

    private readonly object _lifecycleLock = new();
    private readonly GraphicsContext _graphicsContext;
    private readonly DiagnosticStrideOffscreenViewportRenderer _diagnosticRenderer = new();
    private readonly CancellationTokenSource _stopSignal = new();
    private readonly Stopwatch _clock = new();
    private Task? _renderTask;
    private StrideSharedTextureViewportSource? _viewportSource;
    private IStrideOffscreenViewportRenderer? _renderer;
    private bool _isDisposed;
    private ulong _frameNumber;

    public StrideOffscreenViewportRenderLoop(
        GraphicsDevice graphicsDevice,
        IStrideOffscreenViewportRenderer? renderer = null)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _graphicsContext = new GraphicsContext(graphicsDevice);
        _renderer = renderer;
    }

    public string ActiveRendererDescription => (_renderer ?? _diagnosticRenderer).Description;

    public void SetViewportSource(StrideSharedTextureViewportSource? viewportSource)
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _viewportSource = viewportSource;
        }
    }

    public void SetRenderer(IStrideOffscreenViewportRenderer? renderer)
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _renderer = renderer;
        }
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_renderTask != null)
            {
                return;
            }

            _clock.Start();
            _renderTask = Task.Run(RunAsync);
        }
    }

    public void Dispose()
    {
        Task? renderTask;
        lock (_lifecycleLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _stopSignal.Cancel();
            renderTask = _renderTask;
        }

        try
        {
            renderTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PublishRenderLoopFault(exception);
        }

        _clock.Stop();
        _stopSignal.Dispose();
    }

    private async Task RunAsync()
    {
        CancellationToken cancellationToken = _stopSignal.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RenderFrame();
                await Task.Delay(FrameDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishRenderLoopFault(exception);
        }
    }

    private void RenderFrame()
    {
        ulong frameNumber = ++_frameNumber;
        IStrideOffscreenViewportRenderer renderer;
        StrideSharedTextureViewportSource? viewportSource;
        lock (_lifecycleLock)
        {
            renderer = _renderer ?? _diagnosticRenderer;
            viewportSource = _viewportSource;
        }

        viewportSource?.TryRenderOffscreenFrame(
            _graphicsContext,
            renderer,
            frameNumber,
            _clock.Elapsed);
    }

    private void PublishRenderLoopFault(Exception exception)
    {
        StrideSharedTextureViewportSource? viewportSource;
        lock (_lifecycleLock)
        {
            viewportSource = _viewportSource;
        }

        viewportSource?.PublishRenderLoopFault(exception);
    }
}
