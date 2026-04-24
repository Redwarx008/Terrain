#nullable enable

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Stride.Engine;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering.NativeViewport;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

public sealed class NativeStrideViewportViewModel : ObservableObject, IDisposable
{
    private readonly NativeStrideViewportHost _viewportHost;

    public NativeStrideViewportViewModel(NativeStrideViewportHost viewportHost)
    {
        _viewportHost = viewportHost ?? throw new ArgumentNullException(nameof(viewportHost));
        _viewportHost.RuntimeStateChanged += OnRuntimeStateChanged;
    }

    public event EventHandler? StateChanged;

    public NativeStrideViewportHost ViewportHost => _viewportHost;

    public Scene? Scene => _viewportHost.Scene;

    public TerrainManager? TerrainManager => _viewportHost.TerrainManager;

    public bool HasSceneRuntime => _viewportHost.HasSceneRuntime;

    public string Status => _viewportHost.Status;

    public SceneViewMode SceneViewMode => _viewportHost.SceneViewMode;

    public void Dispose()
    {
        _viewportHost.RuntimeStateChanged -= OnRuntimeStateChanged;
    }

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(TerrainManager));
        OnPropertyChanged(nameof(HasSceneRuntime));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(SceneViewMode));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
