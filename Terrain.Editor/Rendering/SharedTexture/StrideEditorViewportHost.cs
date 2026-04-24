#nullable enable

using System;
using Stride.Engine;
using Stride.Graphics;
using Terrain.Editor.Models;
using Terrain.Editor.Services;

namespace Terrain.Editor.Rendering.SharedTexture;

/// <summary>
/// Centralizes the Stride offscreen viewport runtime so ViewModels do not own
/// graphics-device or render-loop lifetimes directly. Future scene/compositor/content
/// services should be attached here instead of being recreated per viewport source.
/// </summary>
public sealed class StrideEditorViewportHost : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly HeadlessStrideGraphicsDeviceHost _graphicsDeviceHost;
    private readonly StrideOffscreenViewportRenderLoop? _renderLoop;
    private readonly StrideSceneViewportRuntime? _sceneRuntime;
    private readonly Scene? _scene;
    private readonly TerrainManager? _terrainManager;
    private StrideSharedTextureViewportSource? _attachedViewportSource;
    private IStrideOffscreenViewportRenderer? _renderer;
    private string _sceneRuntimeStatus;
    private bool _isDisposed;

    public StrideEditorViewportHost(IStrideOffscreenViewportRenderer? renderer = null)
    {
        _renderer = renderer;
        _graphicsDeviceHost = new HeadlessStrideGraphicsDeviceHost();
        _sceneRuntimeStatus = "Stride scene runtime not initialized.";

        if (_graphicsDeviceHost.GraphicsDevice != null)
        {
            try
            {
                _sceneRuntime = new StrideSceneViewportRuntime(_graphicsDeviceHost.GraphicsDevice);
                _scene = _sceneRuntime.Scene;
                _terrainManager = new TerrainManager(_graphicsDeviceHost.GraphicsDevice, _scene);
                _sceneRuntimeStatus =
                    "Stride scene runtime ready; shared host owns a Scene, GraphicsCompositor and TerrainManager.";
                _renderer = _sceneRuntime;
            }
            catch (Exception exception)
            {
                _sceneRuntimeStatus = $"Stride scene runtime initialization failed: {exception.Message}";
            }

            _renderLoop = new StrideOffscreenViewportRenderLoop(_graphicsDeviceHost.GraphicsDevice, renderer ?? _renderer);
            _renderLoop.Start();
        }
        else
        {
            _sceneRuntimeStatus = "Stride scene runtime unavailable because GraphicsDevice initialization failed.";
        }
    }

    public event EventHandler? RuntimeStateChanged;

    public GraphicsDevice? GraphicsDevice => _graphicsDeviceHost.GraphicsDevice;

    public Scene? Scene => _scene;

    public TerrainManager? TerrainManager => _terrainManager;

    public bool IsAvailable => _graphicsDeviceHost.IsAvailable;

    public bool HasSceneRuntime => _scene != null && _terrainManager != null;

    public string GraphicsDeviceStatus => _graphicsDeviceHost.FailureMessage ?? _graphicsDeviceHost.Status;

    public string SceneRuntimeStatus => _sceneRuntimeStatus;

    public string RuntimeStatus => $"{GraphicsDeviceStatus}{Environment.NewLine}{SceneRuntimeStatus}";

    public string ActiveRendererDescription => _renderLoop?.ActiveRendererDescription ?? CreateRendererDescription(_renderer);

    public SceneViewMode SceneViewMode => _sceneRuntime?.SceneViewMode ?? SceneViewMode.Shaded;

    public void AttachViewportSource(StrideSharedTextureViewportSource viewportSource)
    {
        ArgumentNullException.ThrowIfNull(viewportSource);

        StrideSharedTextureViewportSource? previouslyAttached;
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (ReferenceEquals(_attachedViewportSource, viewportSource))
            {
                PublishViewportRuntimeStatus(viewportSource);
                return;
            }

            previouslyAttached = _attachedViewportSource;
            _attachedViewportSource = viewportSource;
            _renderLoop?.SetViewportSource(viewportSource);
        }

        if (previouslyAttached != null)
        {
            previouslyAttached.DetachGraphicsDevice();
        }

        PublishViewportRuntimeStatus(viewportSource);
    }

    public void DetachViewportSource(StrideSharedTextureViewportSource viewportSource)
    {
        ArgumentNullException.ThrowIfNull(viewportSource);

        bool shouldDetach;
        lock (_syncRoot)
        {
            if (_isDisposed || !ReferenceEquals(_attachedViewportSource, viewportSource))
            {
                return;
            }

            _attachedViewportSource = null;
            _renderLoop?.SetViewportSource(null);
            shouldDetach = true;
        }

        if (shouldDetach)
        {
            viewportSource.DetachGraphicsDevice();
        }
    }

    public void SetRenderer(IStrideOffscreenViewportRenderer? renderer)
    {
        StrideSharedTextureViewportSource? attachedViewportSource;
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _renderer = renderer;
            _renderLoop?.SetRenderer(renderer);
            attachedViewportSource = _attachedViewportSource;
        }

        if (attachedViewportSource != null)
        {
            PublishViewportRuntimeStatus(attachedViewportSource);
        }

        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSceneViewMode(SceneViewMode sceneViewMode)
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _sceneRuntime?.ApplySceneViewMode(sceneViewMode);
        }

        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        StrideSharedTextureViewportSource? attachedViewportSource;
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            attachedViewportSource = _attachedViewportSource;
            _attachedViewportSource = null;
            _renderLoop?.SetViewportSource(null);
        }

        if (attachedViewportSource != null)
        {
            attachedViewportSource.DetachGraphicsDevice();
        }

        _renderLoop?.Dispose();
        _terrainManager?.Dispose();
        _sceneRuntime?.Dispose();
        _graphicsDeviceHost.Dispose();
        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishViewportRuntimeStatus(StrideSharedTextureViewportSource viewportSource)
    {
        GraphicsDevice? graphicsDevice = _graphicsDeviceHost.GraphicsDevice;
        viewportSource.SetRuntimeStatus(CreateRuntimeSummary());
        if (graphicsDevice != null)
        {
            viewportSource.AttachGraphicsDevice(graphicsDevice);
            viewportSource.SetDiagnosticStatus(CreateRendererStatus());
            return;
        }

        viewportSource.SetGraphicsDeviceUnavailable(RuntimeStatus);
    }

    private string CreateRuntimeSummary()
    {
        string rendererStatus = _renderer == null
            ? "Renderer: diagnostic clear path active."
            : $"Renderer: bound to {_renderer.Description}.";

        return $"Runtime: {SceneRuntimeStatus}{Environment.NewLine}{rendererStatus}";
    }

    private string CreateRendererStatus()
    {
        return _renderer == null
            ? "Stride offscreen runtime ready; rendering still uses the diagnostic clear renderer."
            : $"Stride offscreen runtime ready; renderer bound: {_renderer.Description}.";
    }

    private static string CreateRendererDescription(IStrideOffscreenViewportRenderer? renderer)
    {
        return renderer?.Description ?? "Diagnostic clear renderer";
    }
}
