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
