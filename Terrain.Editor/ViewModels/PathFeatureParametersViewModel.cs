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
    private int _roadStyleIndex;

    [ObservableProperty]
    private bool _isSketchModeEnabled;

    public bool IsRoadStyleVisible => KindIndex == 0;

    public bool IsRoadCornerSpanVisible => KindIndex == 0;

    public bool IsRiverTerrainShapeVisible => KindIndex == 1;

    public string RoadStyleDisplayName => RoadStyleIndex == (int)PathRoadStyle.Paved ? "Paved" : "Dirt";

    partial void OnKindIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsRoadStyleVisible));
        OnPropertyChanged(nameof(IsRoadCornerSpanVisible));
        OnPropertyChanged(nameof(IsRiverTerrainShapeVisible));
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

    partial void OnRoadStyleIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RoadStyleDisplayName));
        if (!syncing && Enum.IsDefined(typeof(PathRoadStyle), value))
            source.RoadStyle = (PathRoadStyle)value;
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
        RoadStyleIndex = (int)source.RoadStyle;
        IsSketchModeEnabled = source.IsSketchModeEnabled;
        syncing = false;
        OnPropertyChanged(nameof(IsRoadStyleVisible));
        OnPropertyChanged(nameof(IsRoadCornerSpanVisible));
        OnPropertyChanged(nameof(IsRiverTerrainShapeVisible));
        OnPropertyChanged(nameof(RoadStyleDisplayName));
    }
}
