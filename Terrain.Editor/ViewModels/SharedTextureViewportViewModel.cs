#nullable enable

using System;
using Stride.Engine;
using Terrain.Editor.Rendering.SharedTexture;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

public sealed class SharedTextureViewportViewModel : StrideSharedTextureViewportSource
{
    private readonly StrideEditorViewportHost _viewportHost;

    public SharedTextureViewportViewModel(
        StrideEditorViewportHost viewportHost,
        IStrideOffscreenViewportRenderer? sceneRenderer = null)
    {
        _viewportHost = viewportHost ?? throw new ArgumentNullException(nameof(viewportHost));

        if (sceneRenderer != null)
        {
            _viewportHost.SetRenderer(sceneRenderer);
        }

        _viewportHost.RuntimeStateChanged += OnViewportHostRuntimeStateChanged;
        _viewportHost.AttachViewportSource(this);
    }

    public StrideEditorViewportHost ViewportHost => _viewportHost;

    public Scene? Scene => _viewportHost.Scene;

    public TerrainManager? TerrainManager => _viewportHost.TerrainManager;

    public bool HasSceneRuntime => _viewportHost.HasSceneRuntime;

    public string RuntimeStatus => _viewportHost.RuntimeStatus;

    public string ActiveRendererDescription => _viewportHost.ActiveRendererDescription;

    public void SetSceneRenderer(IStrideOffscreenViewportRenderer? sceneRenderer)
    {
        _viewportHost.SetRenderer(sceneRenderer);
    }

    public override void Dispose()
    {
        _viewportHost.RuntimeStateChanged -= OnViewportHostRuntimeStateChanged;
        _viewportHost.DetachViewportSource(this);
        base.Dispose();
    }

    private void OnViewportHostRuntimeStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(TerrainManager));
        OnPropertyChanged(nameof(HasSceneRuntime));
        OnPropertyChanged(nameof(RuntimeStatus));
        OnPropertyChanged(nameof(ActiveRendererDescription));
    }
}
