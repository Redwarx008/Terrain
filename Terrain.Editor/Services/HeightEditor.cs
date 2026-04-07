#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Editor.Rendering;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Commands;

namespace Terrain.Editor.Services;

/// <summary>
/// Orchestrates height editing operations with BeginStroke/ApplyStroke/EndStroke lifecycle.
/// Per D-02: Left mouse button drag applies editing, release ends current stroke.
/// Per D-13: Flatten target = height at click position.
/// Per D-14: Target height held constant during drag.
/// </summary>
public sealed class HeightEditor
{
    // Height conversion constants (must match TerrainManager)
    public const float DefaultHeightScale = 100.0f;
    public const float WorldToUshort = ushort.MaxValue / DefaultHeightScale; // 655.35

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
            flattenTargetHeight = terrainManager.GetRawHeightAtPosition(worldPosition.X, worldPosition.Z);
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

        // Create and begin command for undo/redo
        var command = new HeightEditCommand(terrainManager, toolName);
        HistoryManager.Instance.BeginCommand(command);
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

        terrainManager.MarkDataDirty(TerrainDataChannel.Height, pixelX, pixelZ, brushRadius);

        // Update command region for undo/redo
        HistoryManager.Instance.UpdateCommandRegion(pixelX, pixelZ, brushRadius);
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

        // Commit command for undo/redo
        HistoryManager.Instance.CommitCommand();
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
/// Per D-08: Strength linear mapping to height change speed.
/// Per D-09: Falloff controls strength decay from center to edge.
/// Per D-16, D-17: Boundary clipping (silent, ignore out-of-bounds).
/// </summary>
internal sealed class RaiseTool : IHeightTool
{
    public string Name => "Raise";

    public void Apply(ref HeightEditContext context)
    {
        // D-07: deltaHeight = Strength * FrameTime * Direction (+1 for Raise)
        // Convert world units to ushort: Strength=1 → 50 world units/sec
        float deltaHeight = context.Strength * 50f * HeightEditor.WorldToUshort * context.FrameTime;

        int radius = (int)MathF.Ceiling(context.BrushRadius);

        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = context.CenterX + dx;
                int z = context.CenterZ + dz;

                // D-16, D-17: Boundary clipping
                if (x < 0 || x >= context.DataWidth || z < 0 || z >= context.DataHeight)
                    continue;

                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > context.BrushRadius)
                    continue;

                // D-09: Falloff controls strength decay
                float falloff = HeightEditor.ComputeLinearFalloff(distance, context.BrushRadius, context.BrushInnerRadius);

                int index = z * context.DataWidth + x;
                float currentHeight = context.HeightData[index];
                float newHeight = currentHeight + deltaHeight * falloff;

                // Clamp to ushort range
                context.HeightData[index] = (ushort)Math.Clamp(newHeight, 0, 65535);
            }
        }
    }
}

/// <summary>
/// Lowers terrain height.
/// Per D-07: deltaHeight = Strength * FrameTime * Direction (-1 for Lower).
/// Per D-08: Strength linear mapping to height change speed.
/// Per D-09: Falloff controls strength decay from center to edge.
/// Per D-16, D-17: Boundary clipping (silent, ignore out-of-bounds).
/// </summary>
internal sealed class LowerTool : IHeightTool
{
    public string Name => "Lower";

    public void Apply(ref HeightEditContext context)
    {
        // D-07: deltaHeight = Strength * FrameTime * Direction (-1 for Lower)
        // Convert world units to ushort: Strength=1 → 50 world units/sec
        float deltaHeight = -context.Strength * 50f * HeightEditor.WorldToUshort * context.FrameTime;

        int radius = (int)MathF.Ceiling(context.BrushRadius);

        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = context.CenterX + dx;
                int z = context.CenterZ + dz;

                // D-16, D-17: Boundary clipping
                if (x < 0 || x >= context.DataWidth || z < 0 || z >= context.DataHeight)
                    continue;

                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > context.BrushRadius)
                    continue;

                // D-09: Falloff controls strength decay
                float falloff = HeightEditor.ComputeLinearFalloff(distance, context.BrushRadius, context.BrushInnerRadius);

                int index = z * context.DataWidth + x;
                float currentHeight = context.HeightData[index];
                float newHeight = currentHeight + deltaHeight * falloff;

                // Clamp to ushort range
                context.HeightData[index] = (ushort)Math.Clamp(newHeight, 0, 65535);
            }
        }
    }
}

/// <summary>
/// Smooths terrain heights using Box Blur.
/// Per D-10: Box Blur algorithm - 3x3 kernel for neighbor averaging.
/// Per D-11: Smooth strength controls blend toward average.
/// Per D-12: Partial smooth per frame (not instant).
/// Per D-16, D-17: Boundary clipping (silent, ignore out-of-bounds).
/// </summary>
internal sealed class SmoothTool : IHeightTool
{
    public string Name => "Smooth";

    public void Apply(ref HeightEditContext context)
    {
        // D-10: Box Blur algorithm with 3x3 kernel
        int radius = (int)MathF.Ceiling(context.BrushRadius);

        // Sample radius for blur (3x3 is sufficient for smooth)
        const int blurRadius = 1;

        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = context.CenterX + dx;
                int z = context.CenterZ + dz;

                // D-16, D-17: Boundary clipping
                if (x < 0 || x >= context.DataWidth || z < 0 || z >= context.DataHeight)
                    continue;

                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > context.BrushRadius)
                    continue;

                // D-10: Compute average of neighbors (Box Blur - 3x3 kernel)
                float sum = 0;
                int count = 0;

                for (int bz = -blurRadius; bz <= blurRadius; bz++)
                {
                    for (int bx = -blurRadius; bx <= blurRadius; bx++)
                    {
                        int nx = x + bx;
                        int nz = z + bz;

                        if (nx >= 0 && nx < context.DataWidth && nz >= 0 && nz < context.DataHeight)
                        {
                            sum += context.HeightData[nz * context.DataWidth + nx];
                            count++;
                        }
                    }
                }

                if (count == 0)
                    continue;

                float average = sum / count;

                // D-11: Blend toward average based on Strength
                // D-12: Partial smooth per frame
                float falloff = HeightEditor.ComputeLinearFalloff(distance, context.BrushRadius, context.BrushInnerRadius);
                float blendFactor = context.Strength * context.FrameTime * 5f * falloff;

                int index = z * context.DataWidth + x;
                float currentHeight = context.HeightData[index];
                float newHeight = currentHeight + (average - currentHeight) * blendFactor;

                context.HeightData[index] = (ushort)Math.Clamp(newHeight, 0, 65535);
            }
        }
    }
}

/// <summary>
/// Flattens terrain to a target height.
/// Per D-13: Target = height at click position (passed via constructor).
/// Per D-14: Target height held constant during drag.
/// Per D-15: Blend toward target based on Strength.
/// Per D-16, D-17: Boundary clipping (silent, ignore out-of-bounds).
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
        // D-13: Target height = sampled at click (passed via constructor)
        // D-14: Target height held constant during drag
        // D-15: Blend toward target based on Strength

        int radius = (int)MathF.Ceiling(context.BrushRadius);

        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = context.CenterX + dx;
                int z = context.CenterZ + dz;

                // D-16, D-17: Boundary clipping
                if (x < 0 || x >= context.DataWidth || z < 0 || z >= context.DataHeight)
                    continue;

                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > context.BrushRadius)
                    continue;

                float falloff = HeightEditor.ComputeLinearFalloff(distance, context.BrushRadius, context.BrushInnerRadius);
                float blendFactor = context.Strength * context.FrameTime * 5f * falloff;

                int index = z * context.DataWidth + x;
                float currentHeight = context.HeightData[index];
                float newHeight = currentHeight + (targetHeight - currentHeight) * blendFactor;

                context.HeightData[index] = (ushort)Math.Clamp(newHeight, 0, 65535);
            }
        }
    }
}
