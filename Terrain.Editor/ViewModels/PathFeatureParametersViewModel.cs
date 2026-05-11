#nullable enable

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Terrain.Editor.Services.PathFeatures;

namespace Terrain.Editor.ViewModels;

public sealed partial class PathFeatureParametersViewModel : ObservableObject, IDisposable
{
    private readonly PathFeatureParameters source = PathFeatureParameters.Instance;
    private bool syncing;

    public PathFeatureParametersViewModel()
    {
        SyncFromSource();
        source.ParametersChanged += OnSourceParametersChanged;
    }

    [ObservableProperty]
    private int _kindIndex;

    [ObservableProperty]
    private float _width;

    [ObservableProperty]
    private float _depth;

    [ObservableProperty]
    private float _sideSlope;

    [ObservableProperty]
    private float _cornerSpan;

    [ObservableProperty]
    private int _materialSlotIndex;

    [ObservableProperty]
    private bool _isSketchModeEnabled;

    partial void OnKindIndexChanged(int value)
    {
        if (!syncing)
            source.Kind = value == 1 ? PathFeatureKind.River : PathFeatureKind.Road;
    }

    partial void OnWidthChanged(float value)
    {
        if (!syncing)
            source.Width = value;
    }

    partial void OnDepthChanged(float value)
    {
        if (!syncing)
            source.Depth = value;
    }

    partial void OnSideSlopeChanged(float value)
    {
        if (!syncing)
            source.SideSlope = value;
    }

    partial void OnCornerSpanChanged(float value)
    {
        if (!syncing)
            source.CornerSpan = value;
    }

    partial void OnMaterialSlotIndexChanged(int value)
    {
        if (!syncing)
            source.MaterialSlotIndex = value;
    }

    partial void OnIsSketchModeEnabledChanged(bool value)
    {
        if (!syncing)
            source.IsSketchModeEnabled = value;
    }

    public void Dispose()
    {
        source.ParametersChanged -= OnSourceParametersChanged;
    }

    private void OnSourceParametersChanged(object? sender, EventArgs e)
    {
        SyncFromSource();
    }

    private void SyncFromSource()
    {
        syncing = true;
        KindIndex = source.Kind == PathFeatureKind.River ? 1 : 0;
        Width = source.Width;
        Depth = source.Depth;
        SideSlope = source.SideSlope;
        CornerSpan = source.CornerSpan;
        MaterialSlotIndex = source.MaterialSlotIndex;
        IsSketchModeEnabled = source.IsSketchModeEnabled;
        syncing = false;
    }
}
