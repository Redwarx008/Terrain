#nullable enable

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

public sealed partial class BrushParametersViewModel : ObservableObject, IDisposable
{
    private readonly BrushParameters _source = BrushParameters.Instance;

    public BrushParametersViewModel()
    {
        SyncFromSource();
        _source.ParametersChanged += OnSourceParametersChanged;
    }

    [ObservableProperty]
    private float _size;

    [ObservableProperty]
    private float _strength;

    [ObservableProperty]
    private float _falloff;

    [ObservableProperty]
    private bool _useSlopeFilter;

    [ObservableProperty]
    private float _minSlopeDegrees;

    [ObservableProperty]
    private float _maxSlopeDegrees;

    partial void OnSizeChanged(float value) => _source.Size = value;
    partial void OnStrengthChanged(float value) => _source.Strength = value;
    partial void OnFalloffChanged(float value) => _source.Falloff = value;
    partial void OnUseSlopeFilterChanged(bool value) => _source.UseSlopeFilter = value;
    partial void OnMinSlopeDegreesChanged(float value) => _source.MinSlopeDegrees = value;
    partial void OnMaxSlopeDegreesChanged(float value) => _source.MaxSlopeDegrees = value;

    public void Dispose()
    {
        _source.ParametersChanged -= OnSourceParametersChanged;
    }

    private void OnSourceParametersChanged(object? sender, EventArgs e)
    {
        SyncFromSource();
    }

    private void SyncFromSource()
    {
        Size = _source.Size;
        Strength = _source.Strength;
        Falloff = _source.Falloff;
        UseSlopeFilter = _source.UseSlopeFilter;
        MinSlopeDegrees = _source.MinSlopeDegrees;
        MaxSlopeDegrees = _source.MaxSlopeDegrees;
    }
}