#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

/// <summary>
/// Wraps a single <see cref="ClimateRuleLayer"/> for Avalonia data binding.
/// Property changes are committed to the backend service immediately.
/// </summary>
public sealed partial class RuleViewModel : ObservableObject
{
    private readonly ClimateRuleLayer _source;
    private int _globalIndex;
    private bool _syncing;

    public RuleViewModel(ClimateRuleLayer source, int globalIndex)
    {
        _source = source;
        _globalIndex = globalIndex;
        SyncFromSource(globalIndex);
    }

    public int Id => _source.Id;

    public int ClimateId => _source.ClimateId;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private float _minAltitude;

    [ObservableProperty]
    private float _maxAltitude;

    [ObservableProperty]
    private float _minSlopeDegrees;

    [ObservableProperty]
    private float _maxSlopeDegrees;

    [ObservableProperty]
    private float _blendRange;

    [ObservableProperty]
    private int _materialSlotIndex;

    partial void OnNameChanged(string value)
    {
        if (_syncing) return;
        if (_source.Name != value)
        {
            _source.Name = value;
            ClimateRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing) return;
        if (_source.Enabled != value)
        {
            _source.Enabled = value;
            ClimateRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_syncing) return;
        if (_source.Visible != value)
        {
            _source.Visible = value;
            ClimateRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnMinAltitudeChanged(float value)
    {
        if (_syncing) return;
        ClimateRuleService.Instance.SetRuleAltitude(_globalIndex, value, null);
    }

    partial void OnMaxAltitudeChanged(float value)
    {
        if (_syncing) return;
        ClimateRuleService.Instance.SetRuleAltitude(_globalIndex, null, value);
    }

    partial void OnMinSlopeDegreesChanged(float value)
    {
        if (_syncing) return;
        ClimateRuleService.Instance.SetRuleSlope(_globalIndex, value, null);
    }

    partial void OnMaxSlopeDegreesChanged(float value)
    {
        if (_syncing) return;
        ClimateRuleService.Instance.SetRuleSlope(_globalIndex, null, value);
    }

    partial void OnBlendRangeChanged(float value)
    {
        if (_syncing) return;
        if (_source.BlendRange != value)
        {
            _source.BlendRange = value;
            ClimateRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnMaterialSlotIndexChanged(int value)
    {
        if (_syncing) return;
        if (_source.MaterialSlotIndex != value)
        {
            _source.MaterialSlotIndex = value;
            ClimateRuleService.Instance.NotifyMutated();
        }
    }

    /// <summary>
    /// Refreshes properties from the source model.
    /// </summary>
    public void SyncFromSource(int globalIndex)
    {
        _syncing = true;
        _globalIndex = globalIndex;
        Name = _source.Name;
        IsEnabled = _source.Enabled;
        IsVisible = _source.Visible;
        MinAltitude = _source.MinAltitude;
        MaxAltitude = _source.MaxAltitude;
        MinSlopeDegrees = _source.MinSlopeDegrees;
        MaxSlopeDegrees = _source.MaxSlopeDegrees;
        BlendRange = _source.BlendRange;
        MaterialSlotIndex = _source.MaterialSlotIndex;
        _syncing = false;
    }
}
