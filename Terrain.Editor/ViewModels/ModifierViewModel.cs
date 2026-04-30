#nullable enable

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

/// <summary>
/// Wraps a single <see cref="BiomeModifier"/> for Avalonia data binding.
/// Property changes are committed to the backend service immediately.
/// </summary>
public sealed partial class ModifierViewModel : ObservableObject
{
    private readonly BiomeModifier _source;
    private bool _syncing;

    public ModifierViewModel(BiomeModifier source)
    {
        _source = source;
        SyncFromSource();
    }

    public int Id => _source.Id;

    public BiomeModifierType ModifierType => _source.Type;

    public string TypeDisplayName => _source.Type switch
    {
        BiomeModifierType.HeightRange => "Height",
        BiomeModifierType.SlopeRange => "Slope",
        BiomeModifierType.CurvatureRange => "Curvature",
        BiomeModifierType.DirectionRange => "Direction",
        BiomeModifierType.Noise => "Noise",
        BiomeModifierType.TextureMask => "Texture Mask",
        _ => _source.Type.ToString()
    };

    public int OpacityPercent => (int)Math.Round(Opacity * 100.0f);

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private int _blendModeIndex;

    [ObservableProperty]
    private float _opacity = 1.0f;

    [ObservableProperty]
    private float _min;

    [ObservableProperty]
    private float _max = 1.0f;

    [ObservableProperty]
    private float _minFalloff;

    [ObservableProperty]
    private float _maxFalloff;

    [ObservableProperty]
    private float _radius = 1.0f;

    [ObservableProperty]
    private float _angleDegrees;

    [ObservableProperty]
    private float _angleRangeDegrees = 180.0f;

    [ObservableProperty]
    private float _scale = 1.0f;

    [ObservableProperty]
    private float _offsetX;

    [ObservableProperty]
    private float _offsetY;

    [ObservableProperty]
    private float _seed;

    [ObservableProperty]
    private float _octaves = 4.0f;

    [ObservableProperty]
    private bool _isInverted;

    // Visibility helpers for conditional UI
    public bool HasMin => _source.Type is BiomeModifierType.HeightRange or BiomeModifierType.SlopeRange or BiomeModifierType.CurvatureRange or BiomeModifierType.DirectionRange or BiomeModifierType.Noise;
    public bool HasMax => HasMin;
    public bool HasFalloff => _source.Type is BiomeModifierType.HeightRange or BiomeModifierType.SlopeRange or BiomeModifierType.CurvatureRange;
    public bool HasRadius => _source.Type is BiomeModifierType.CurvatureRange;
    public bool HasAngle => _source.Type is BiomeModifierType.DirectionRange;
    public bool HasScale => _source.Type is BiomeModifierType.Noise;
    public bool HasOffset => _source.Type is BiomeModifierType.Noise;
    public bool HasSeed => _source.Type is BiomeModifierType.Noise;
    public bool HasOctaves => _source.Type is BiomeModifierType.Noise;
    public bool HasInvert => _source.Type is not BiomeModifierType.TextureMask;

    // Range helpers for slider min/max
    public float MinSliderMinimum => _source.Type switch
    {
        BiomeModifierType.HeightRange => 0,
        BiomeModifierType.SlopeRange => 0,
        BiomeModifierType.CurvatureRange => -1,
        BiomeModifierType.DirectionRange => -180,
        BiomeModifierType.Noise => 0,
        _ => 0
    };
    public float MinSliderMaximum => _source.Type switch
    {
        BiomeModifierType.HeightRange => 1000,
        BiomeModifierType.SlopeRange => 90,
        BiomeModifierType.CurvatureRange => 1,
        BiomeModifierType.DirectionRange => 180,
        BiomeModifierType.Noise => 1,
        _ => 1
    };
    public float MaxSliderMinimum => MinSliderMinimum;
    public float MaxSliderMaximum => MinSliderMaximum;

    partial void OnNameChanged(string value)
    {
        if (_syncing) return;
        if (_source.Name != value)
        {
            _source.Name = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing) return;
        if (_source.Enabled != value)
        {
            _source.Enabled = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnBlendModeIndexChanged(int value)
    {
        if (_syncing) return;
        var mode = (BiomeModifierBlendMode)value;
        if (_source.BlendMode != mode)
        {
            _source.BlendMode = mode;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnOpacityChanged(float value)
    {
        if (_syncing) return;
        value = Math.Clamp(value, 0f, 1f);
        if (MathF.Abs(_source.Opacity - value) > 0.001f)
        {
            _source.Opacity = value;
            OnPropertyChanged(nameof(OpacityPercent));
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnMinChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.Min - value) > 0.001f)
        {
            _source.Min = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnMaxChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.Max - value) > 0.001f)
        {
            _source.Max = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnMinFalloffChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.MinFalloff - value) > 0.001f)
        {
            _source.MinFalloff = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnMaxFalloffChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.MaxFalloff - value) > 0.001f)
        {
            _source.MaxFalloff = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnRadiusChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.Radius - value) > 0.001f)
        {
            _source.Radius = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnAngleDegreesChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.AngleDegrees - value) > 0.001f)
        {
            _source.AngleDegrees = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnAngleRangeDegreesChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.AngleRangeDegrees - value) > 0.001f)
        {
            _source.AngleRangeDegrees = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnScaleChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.Scale - value) > 0.001f)
        {
            _source.Scale = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnOffsetXChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.OffsetX - value) > 0.001f)
        {
            _source.OffsetX = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnOffsetYChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.OffsetY - value) > 0.001f)
        {
            _source.OffsetY = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnSeedChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.Seed - value) > 0.001f)
        {
            _source.Seed = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnOctavesChanged(float value)
    {
        if (_syncing) return;
        if (MathF.Abs(_source.Octaves - value) > 0.001f)
        {
            _source.Octaves = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnIsInvertedChanged(bool value)
    {
        if (_syncing) return;
        float newInvert = value ? 1.0f : 0.0f;
        if (MathF.Abs(_source.Invert - newInvert) > 0.001f)
        {
            _source.Invert = newInvert;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    public void SyncFromSource()
    {
        _syncing = true;
        Name = _source.Name;
        IsEnabled = _source.Enabled;
        BlendModeIndex = (int)_source.BlendMode;
        Opacity = _source.Opacity;
        OnPropertyChanged(nameof(OpacityPercent));
        Min = _source.Min;
        Max = _source.Max;
        MinFalloff = _source.MinFalloff;
        MaxFalloff = _source.MaxFalloff;
        Radius = _source.Radius;
        AngleDegrees = _source.AngleDegrees;
        AngleRangeDegrees = _source.AngleRangeDegrees;
        Scale = _source.Scale;
        OffsetX = _source.OffsetX;
        OffsetY = _source.OffsetY;
        Seed = _source.Seed;
        Octaves = _source.Octaves;
        IsInverted = _source.Invert > 0.5f;
        _syncing = false;
    }
}
