#nullable enable

using System;

namespace Terrain.Editor.Services.PathFeatures;

public sealed class PathFeatureParameters
{
    private static readonly Lazy<PathFeatureParameters> InstanceFactory = new(() => new PathFeatureParameters());

    private PathFeatureKind _kind = PathFeatureKind.Road;
    private float _width = 8.0f;
    private float _depth;
    private float _sideSlope = 4.0f;
    private float _cornerSpan = 0.35f;
    private int _materialSlotIndex;
    private bool _isSketchModeEnabled;

    public static PathFeatureParameters Instance => InstanceFactory.Value;

    public PathFeatureKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value)
                return;

            _kind = value;
            if (_kind == PathFeatureKind.River && _depth <= 0.0f)
                _depth = 2.0f;
            if (_kind == PathFeatureKind.Road && _depth < 0.0f)
                _depth = 0.0f;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public float Width
    {
        get => _width;
        set
        {
            float clamped = Math.Clamp(value, 1.0f, 128.0f);
            if (Math.Abs(_width - clamped) < 0.001f)
                return;

            _width = clamped;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public float Depth
    {
        get => _depth;
        set
        {
            float clamped = Math.Clamp(value, 0.0f, 64.0f);
            if (Math.Abs(_depth - clamped) < 0.001f)
                return;

            _depth = clamped;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public float SideSlope
    {
        get => _sideSlope;
        set
        {
            float clamped = Math.Clamp(value, 0.1f, 64.0f);
            if (Math.Abs(_sideSlope - clamped) < 0.001f)
                return;

            _sideSlope = clamped;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public float CornerSpan
    {
        get => _cornerSpan;
        set
        {
            float clamped = Math.Clamp(value, 0.05f, 1.0f);
            if (Math.Abs(_cornerSpan - clamped) < 0.001f)
                return;

            _cornerSpan = clamped;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int MaterialSlotIndex
    {
        get => _materialSlotIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, 255);
            if (_materialSlotIndex == clamped)
                return;

            _materialSlotIndex = clamped;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsSketchModeEnabled
    {
        get => _isSketchModeEnabled;
        set
        {
            if (_isSketchModeEnabled == value)
                return;

            _isSketchModeEnabled = value;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ParametersChanged;

    public PathFeatureStyle CreateStyle()
    {
        return new PathFeatureStyle
        {
            Width = Width,
            Depth = Depth,
            SideSlope = SideSlope,
            CornerSpan = CornerSpan,
            MaterialSlotIndex = MaterialSlotIndex,
        };
    }

    private PathFeatureParameters()
    {
    }
}
