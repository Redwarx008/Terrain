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

    // === 新增材质绘制参数 ===
    private float _weight = 1.0f;          // 权重，默认最大
    private bool _randomRotation = false;  // 随机旋转 (默认禁用)
    private float _fixedRotationDegrees = 0.0f; // 固定旋转角度
    private bool _use3DProjection = false; // 3D 投影开关

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
    /// 材质权重 (0-1)。
    /// 控制材质混合的权重，1 = 中心域，0 = 边缘域。
    /// </summary>
    public float Weight
    {
        get => _weight;
        set
        {
            if (_weight != value)
            {
                _weight = Math.Clamp(value, 0.0f, 1.0f);
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 是否启用随机旋转。
    /// 打破纹理平铺重复。
    /// </summary>
    public bool RandomRotation
    {
        get => _randomRotation;
        set
        {
            if (_randomRotation != value)
            {
                _randomRotation = value;
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 固定旋转角度 (度数，0-360)。
    /// 仅在 RandomRotation = false 时使用。
    /// </summary>
    public float FixedRotationDegrees
    {
        get => _fixedRotationDegrees;
        set
        {
            if (_fixedRotationDegrees != value)
            {
                _fixedRotationDegrees = Math.Clamp(value, 0.0f, 360.0f);
                ParametersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 是否启用 3D 投影。
    /// 解决悬崖纹理拉伸问题。
    /// </summary>
    public bool Use3DProjection
    {
        get => _use3DProjection;
        set
        {
            if (_use3DProjection != value)
            {
                _use3DProjection = value;
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
