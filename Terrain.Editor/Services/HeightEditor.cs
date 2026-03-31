#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Editor.Services;

namespace Terrain.Editor.Services;

/// <summary>
/// Orchestrates height editing operations with BeginStroke/ApplyStroke/EndStroke lifecycle.
/// Per D-02: Left mouse button drag applies editing, release ends current stroke.
/// Per D-13: Flatten target = height at click position.
/// Per D-14: Target height held constant during drag.
/// </summary>
public sealed class HeightEditor
{
    private static readonly Lazy<HeightEditor> _instance = new(() => new());

    /// <summary>
    /// Gets the singleton instance of HeightEditor.
    /// </summary>
    public static HeightEditor Instance => _instance.Value;

    private IHeightTool? currentTool;
    private float? flattenTargetHeight;
    private bool isStrokeActive;

    /// <summary>
    /// Begins a new editing stroke.
    /// Per D-13: For Flatten tool, samples target height at click position.
    /// </summary>
    /// <param name="toolName">Name of the tool to use ("Raise", "Lower", "Smooth", "Flatten").</param>
    /// <param name="worldPosition">World position where the stroke begins.</param>
    /// <param name="terrainManager">Terrain manager for height queries.</param>
    public void BeginStroke(string toolName, Vector3 worldPosition, TerrainManager terrainManager)
    {
        isStrokeActive = true;

        // D-13: For Flatten tool, sample target height at click position
        if (toolName == "Flatten")
        {
            flattenTargetHeight = terrainManager.GetHeightAtPosition(worldPosition.X, worldPosition.Z);
        }

        // Create appropriate tool instance
        currentTool = toolName switch
        {
            "Raise" => new RaiseTool(),
            "Lower" => new LowerTool(),
            "Smooth" => new SmoothTool(),
            "Flatten" => new FlattenTool(flattenTargetHeight ?? 0f),
            _ => throw new ArgumentException($"Unknown tool: {toolName}", nameof(toolName))
        };
    }

    /// <summary>
    /// Applies the current stroke at the given position.
    /// Per D-03: Updates affected pixels each frame.
    /// </summary>
    /// <param name="worldPosition">Current world position of the brush.</param>
    /// <param name="terrainManager">Terrain manager for height data access.</param>
    /// <param name="frameTime">Frame delta time for frame-rate independent editing.</param>
    public void ApplyStroke(Vector3 worldPosition, TerrainManager terrainManager, float frameTime)
    {
        if (!isStrokeActive || currentTool == null)
            return;

        // Get height data cache - TODO: needs HeightDataCache property on TerrainManager (Plan 02)
        var heightData = terrainManager.HeightDataCache;
        if (heightData == null)
            return;

        // Convert world position to pixel coordinates
        int pixelX = (int)MathF.Round(worldPosition.X);
        int pixelZ = (int)MathF.Round(worldPosition.Z);

        // Get brush parameters
        var brushParams = BrushParameters.Instance;
        float brushRadius = brushParams.Size * 0.5f;
        float brushInnerRadius = brushRadius * brushParams.EffectiveFalloff;

        // Build edit context
        var context = new HeightEditContext
        {
            HeightData = heightData,
            DataWidth = terrainManager.HeightCacheWidth,
            DataHeight = terrainManager.HeightCacheHeight,
            CenterX = pixelX,
            CenterZ = pixelZ,
            BrushRadius = brushRadius,
            BrushInnerRadius = brushInnerRadius,
            Strength = brushParams.Strength,
            FrameTime = frameTime
        };

        // Apply the tool
        currentTool.Apply(ref context);

        // Sync to GPU - TODO: implemented in Plan 02
        terrainManager.UpdateHeightData();
    }

    /// <summary>
    /// Ends the current editing stroke.
    /// Per D-14: Clears target height (for Flatten tool).
    /// </summary>
    public void EndStroke()
    {
        isStrokeActive = false;
        currentTool = null;
        flattenTargetHeight = null;
    }

    /// <summary>
    /// Computes linear falloff for brush strength.
    /// Per D-09: 100% strength inside inner radius, 0% at outer radius.
    /// </summary>
    /// <param name="distance">Distance from brush center.</param>
    /// <param name="outerRadius">Outer brush radius.</param>
    /// <param name="innerRadius">Inner brush radius (100% strength area).</param>
    /// <returns>Falloff factor (0-1).</returns>
    public static float ComputeLinearFalloff(float distance, float outerRadius, float innerRadius)
    {
        if (distance <= innerRadius)
            return 1.0f;  // 100% strength inside inner radius

        if (distance >= outerRadius)
            return 0.0f;  // 0% strength at/after outer radius

        return 1.0f - (distance - innerRadius) / (outerRadius - innerRadius);
    }
}

/// <summary>
/// Raises terrain height.
/// Per D-07: deltaHeight = Strength * FrameTime * Direction (+1 for Raise).
/// Per D-09: Falloff controls strength decay from center to edge.
/// </summary>
internal sealed class RaiseTool : IHeightTool
{
    public string Name => "Raise";

    public void Apply(ref HeightEditContext context)
    {
        throw new NotImplementedException("Implemented in Plan 02");
    }
}

/// <summary>
/// Lowers terrain height.
/// Per D-07: deltaHeight = Strength * FrameTime * Direction (-1 for Lower).
/// Per D-09: Falloff controls strength decay from center to edge.
/// </summary>
internal sealed class LowerTool : IHeightTool
{
    public string Name => "Lower";

    public void Apply(ref HeightEditContext context)
    {
        throw new NotImplementedException("Implemented in Plan 02");
    }
}

/// <summary>
/// Smooths terrain heights using Box Blur.
/// Per D-10: Box Blur algorithm - average of neighbors.
/// Per D-11: Strength controls blend toward average.
/// Per D-12: Partial smooth per frame.
/// </summary>
internal sealed class SmoothTool : IHeightTool
{
    public string Name => "Smooth";

    public void Apply(ref HeightEditContext context)
    {
        throw new NotImplementedException("Implemented in Plan 02");
    }
}

/// <summary>
/// Flattens terrain to a target height.
/// Per D-13: Target = height at click position.
/// Per D-14: Target held constant during drag.
/// Per D-15: Blend toward target based on Strength.
/// </summary>
internal sealed class FlattenTool : IHeightTool
{
    private readonly float targetHeight;

    public FlattenTool(float targetHeight)
    {
        this.targetHeight = targetHeight;
    }

    public string Name => "Flatten";

    public void Apply(ref HeightEditContext context)
    {
        throw new NotImplementedException("Implemented in Plan 02");
    }
}
