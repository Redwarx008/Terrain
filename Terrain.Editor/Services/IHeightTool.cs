#nullable enable

namespace Terrain.Editor.Services;

/// <summary>
/// Context passed to height editing tools during Apply operations.
/// Passed by ref for performance (avoids struct copy on each call).
/// </summary>
public readonly struct HeightEditContext
{
    /// <summary>
    /// Mutable height array reference. Tools modify this directly.
    /// </summary>
    public ushort[] HeightData { get; init; }

    /// <summary>
    /// Width of the heightmap in pixels.
    /// </summary>
    public int DataWidth { get; init; }

    /// <summary>
    /// Height of the heightmap in pixels.
    /// </summary>
    public int DataHeight { get; init; }

    /// <summary>
    /// Brush center X coordinate in pixel space.
    /// </summary>
    public int CenterX { get; init; }

    /// <summary>
    /// Brush center Z coordinate in pixel space.
    /// </summary>
    public int CenterZ { get; init; }

    /// <summary>
    /// Brush outer radius in pixels.
    /// </summary>
    public float BrushRadius { get; init; }

    /// <summary>
    /// Brush inner radius in pixels (100% strength area).
    /// Calculated as: Size * 0.5f * EffectiveFalloff.
    /// </summary>
    public float BrushInnerRadius { get; init; }

    /// <summary>
    /// Brush strength (0-1).
    /// </summary>
    public float Strength { get; init; }

    /// <summary>
    /// Frame delta time for frame-rate independent editing.
    /// </summary>
    public float FrameTime { get; init; }
}

/// <summary>
/// Interface for height modification tools.
/// Implements the Strategy pattern for tool behavior.
/// </summary>
public interface IHeightTool
{
    /// <summary>
    /// Gets the tool name for display and identification.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Applies the tool effect to the height data.
    /// </summary>
    /// <param name="context">Edit context containing height data and brush parameters.</param>
    void Apply(ref HeightEditContext context);
}
