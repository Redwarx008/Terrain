#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Singleton service holding shared brush parameters for terrain editing.
/// Multiple consumers (UI panels, viewport preview, editing system) bind to this.
/// </summary>
public sealed class BrushParameters
{
    private static readonly Lazy<BrushParameters> _instance = new(() => new());
    public static BrushParameters Instance => _instance.Value;

    // Per CONTEXT.md D-04, D-05, D-06
    private float _size = 30.0f;           // Default 30, range 1-200
    private float _strength = 0.5f;        // Default 0.5, range 0-1
    private float _falloff = 0.5f;         // Default 0.5, range 0-1, INVERTED: 1=hard, 0=soft
    private int _selectedBrushIndex = 0;   // Circle is index 0

    // === 坡度过滤参数 ===
    private bool _useSlopeFilter = false;          // 坡度过滤开关
    private float _minSlopeDegrees = 0.0f;          // 最小坡度（度）
    private float _maxSlopeDegrees = 90.0f;         // 最大坡度（度）

    public float Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = Math.Clamp(value, 1.0f, 200.0f);
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public float Strength
    {
        get => _strength;
        set
        {
            if (_strength != value)
            {
                _strength = Math.Clamp(value, 0.0f, 1.0f);
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public float Falloff
    {
        get => _falloff;
        set
        {
            if (_falloff != value)
            {
                _falloff = Math.Clamp(value, 0.0f, 1.0f);
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int SelectedBrushIndex
    {
        get => _selectedBrushIndex;
        set
        {
            if (_selectedBrushIndex != value)
            {
                _selectedBrushIndex = value;
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 是否启用坡度过滤。
    /// 启用后，只在坡度处于 [MinSlopeDegrees, MaxSlopeDegrees] 范围内时绘制。
    /// </summary>
    public bool UseSlopeFilter
    {
        get => _useSlopeFilter;
        set
        {
            if (_useSlopeFilter != value)
            {
                _useSlopeFilter = value;
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 最小坡度角度 (度数，0-90)。
    /// 仅在 UseSlopeFilter = true 时使用。
    /// 0 = 平地，90 = 垂直悬崖。
    /// </summary>
    public float MinSlopeDegrees
    {
        get => _minSlopeDegrees;
        set
        {
            float clamped = Math.Clamp(value, 0.0f, 90.0f);
            if (_minSlopeDegrees != clamped)
            {
                _minSlopeDegrees = clamped;
                // 联动：Min 增大时自动推高 Max
                if (_maxSlopeDegrees < clamped)
                    _maxSlopeDegrees = clamped;
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 最大坡度角度 (度数，0-90)。
    /// 仅在 UseSlopeFilter = true 时使用。
    /// </summary>
    public float MaxSlopeDegrees
    {
        get => _maxSlopeDegrees;
        set
        {
            float clamped = Math.Clamp(value, 0.0f, 90.0f);
            if (_maxSlopeDegrees != clamped)
            {
                _maxSlopeDegrees = clamped;
                // 联动：Max 减小时自动推低 Min
                if (_minSlopeDegrees > clamped)
                    _minSlopeDegrees = clamped;
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Raised when any brush parameter changes.
    /// </summary>
    public event EventHandler? ParametersChanged;

    /// <summary>
    /// Gets the effective falloff for brush calculations.
    /// Per D-06: inverted semantics where 1 = hard edge, 0 = soft edge.
    /// The inner circle radius = outerRadius * EffectiveFalloff.
    /// </summary>
    public float EffectiveFalloff => 1.0f - Falloff;

    private BrushParameters() { }
}
