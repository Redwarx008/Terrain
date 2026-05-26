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
    private PathRoadStyle _roadStyle = PathRoadStyle.Dirt;
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

    public PathRoadStyle RoadStyle
    {
        get => _roadStyle;
        set
        {
            if (_roadStyle == value)
                return;

            _roadStyle = value;
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
            Depth = _depth,
            SideSlope = SideSlope,
            CornerSpan = CornerSpan,
            RoadStyle = RoadStyle,
        };
    }

    public void LoadFromFeature(PathFeatureKind kind, PathFeatureStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        bool changed = false;
        float width = Math.Clamp(style.Width, 1.0f, 128.0f);
        float depth = Math.Clamp(style.Depth, 0.0f, 64.0f);
        float sideSlope = Math.Clamp(style.SideSlope, 0.1f, 64.0f);
        float cornerSpan = Math.Clamp(style.CornerSpan, 0.05f, 1.0f);

        if (_kind != kind)
        {
            _kind = kind;
            changed = true;
        }

        if (Math.Abs(_width - width) >= 0.001f)
        {
            _width = width;
            changed = true;
        }

        if (Math.Abs(_depth - depth) >= 0.001f)
        {
            _depth = depth;
            changed = true;
        }

        if (Math.Abs(_sideSlope - sideSlope) >= 0.001f)
        {
            _sideSlope = sideSlope;
            changed = true;
        }

        if (Math.Abs(_cornerSpan - cornerSpan) >= 0.001f)
        {
            _cornerSpan = cornerSpan;
            changed = true;
        }

        if (_roadStyle != style.RoadStyle)
        {
            _roadStyle = style.RoadStyle;
            changed = true;
        }

        if (changed)
            ParametersChanged?.Invoke(this, EventArgs.Empty);
    }

    private PathFeatureParameters()
    {
    }
}
