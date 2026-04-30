#nullable enable

using System;
using System.Numerics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

/// <summary>
/// Wraps a single <see cref="BiomeDefinition"/> for Avalonia data binding.
/// </summary>
public sealed partial class BiomeDefinitionViewModel : ObservableObject
{
    private readonly BiomeDefinition _source;
    private bool _syncing;

    public BiomeDefinitionViewModel(BiomeDefinition source)
    {
        _source = source;
        SyncFromSource();
    }

    public int Id => _source.Id;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private ISolidColorBrush _debugColorBrush = Brushes.White;

    partial void OnNameChanged(string value)
    {
        if (_syncing) return;
        if (_source.Name != value)
        {
            _source.Name = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnDebugColorBrushChanged(ISolidColorBrush value)
    {
        if (_syncing) return;
        var color = value.Color;
        var vector = new Vector4(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);

        if (_source.DebugColor != vector)
        {
            _source.DebugColor = vector;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    /// <summary>
    /// Refreshes properties from the source model.
    /// </summary>
    public void SyncFromSource()
    {
        _syncing = true;
        Name = _source.Name;
        DebugColorBrush = ToBrush(_source.DebugColor);
        _syncing = false;
    }

    private static SolidColorBrush ToBrush(Vector4 v)
    {
        return new SolidColorBrush(new Avalonia.Media.Color(
            (byte)Math.Clamp((int)(v.W * 255f), 0, 255),
            (byte)Math.Clamp((int)(v.X * 255f), 0, 255),
            (byte)Math.Clamp((int)(v.Y * 255f), 0, 255),
            (byte)Math.Clamp((int)(v.Z * 255f), 0, 255)));
    }
}
