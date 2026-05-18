#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;

namespace Terrain.Editor.Services;

/// <summary>
/// Rasterizes an ordered river stroke directly into the authored RiverMask.
/// The first version writes a solid ribbon so editor input no longer depends on PathFeature.
/// </summary>
public sealed class RiverMaskWriter
{
    private const float MaskResolutionRatio = 2.0f;
    private static readonly Lazy<RiverMaskWriter> InstanceFactory = new(() => new RiverMaskWriter());

    public static RiverMaskWriter Instance => InstanceFactory.Value;

    private RiverMaskWriter()
    {
    }

    public static float GetMaskRadius(float width)
    {
        float clampedWidth = Math.Clamp(width, 1.0f, 128.0f);
        return clampedWidth * 0.5f / MaskResolutionRatio;
    }

    public static float GetQuantizedWorldPreviewRadius(float width)
    {
        float maskRadius = GetMaskRadius(width);
        int ceilRadius = (int)MathF.Ceiling(maskRadius);
        int maxMaskOffset = 0;

        for (int dz = -ceilRadius; dz <= ceilRadius; dz++)
        {
            for (int dx = -ceilRadius; dx <= ceilRadius; dx++)
            {
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > maskRadius)
                    continue;

                maxMaskOffset = Math.Max(maxMaskOffset, Math.Max(Math.Abs(dx), Math.Abs(dz)));
            }
        }

        return (maxMaskOffset + 0.5f) * MaskResolutionRatio;
    }

    public static void VisitStrokeMaskSamples(IReadOnlyList<Vector3> worldPoints, Action<float, float> visitor)
    {
        ArgumentNullException.ThrowIfNull(worldPoints);
        ArgumentNullException.ThrowIfNull(visitor);
        if (worldPoints.Count == 0)
            return;

        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 start = worldPoints[i];
            Vector3 end = i + 1 < worldPoints.Count ? worldPoints[i + 1] : worldPoints[i];
            VisitSegmentMaskSamples(start, end, visitor);
        }
    }

    public bool ApplyStroke(IReadOnlyList<Vector3> worldPoints, float width, RiverMask mask, TerrainManager terrainManager, byte riverValue = 255, bool markProjectDirty = true)
    {
        if (worldPoints.Count == 0)
            return false;

        float halfResRadius = GetMaskRadius(width);
        int ceilRadius = (int)MathF.Ceiling(halfResRadius);
        bool changed = false;
        int minHeightX = int.MaxValue;
        int minHeightY = int.MaxValue;
        int maxHeightX = int.MinValue;
        int maxHeightY = int.MinValue;

        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 start = worldPoints[i];
            Vector3 end = i + 1 < worldPoints.Count ? worldPoints[i + 1] : worldPoints[i];
            changed |= RasterizeSegment(start, end, mask, riverValue, halfResRadius, ceilRadius, ref minHeightX, ref minHeightY, ref maxHeightX, ref maxHeightY);
        }

        if (!changed)
            return false;

        terrainManager.MarkRiverMaskDirty(markProjectDirty);
        return true;
    }

    private static void VisitSegmentMaskSamples(Vector3 start, Vector3 end, Action<float, float> visitor)
    {
        int startHeightX = (int)MathF.Round(start.X);
        int startHeightY = (int)MathF.Round(start.Z);
        int endHeightX = (int)MathF.Round(end.X);
        int endHeightY = (int)MathF.Round(end.Z);

        float startMaskX = startHeightX * 0.5f;
        float startMaskY = startHeightY * 0.5f;
        float endMaskX = endHeightX * 0.5f;
        float endMaskY = endHeightY * 0.5f;

        float dx = endMaskX - startMaskX;
        float dy = endMaskY - startMaskY;
        float segmentLength = MathF.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)MathF.Ceiling(segmentLength));

        for (int step = 0; step <= steps; step++)
        {
            float t = steps == 0 ? 0.0f : step / (float)steps;
            float centerX = Lerp(startMaskX, endMaskX, t);
            float centerY = Lerp(startMaskY, endMaskY, t);
            visitor(centerX, centerY);
        }
    }

    private static bool RasterizeSegment(
        Vector3 start,
        Vector3 end,
        RiverMask mask,
        byte riverValue,
        float halfResRadius,
        int ceilRadius,
        ref int minHeightX,
        ref int minHeightY,
        ref int maxHeightX,
        ref int maxHeightY)
    {
        int startHeightX = (int)MathF.Round(start.X);
        int startHeightY = (int)MathF.Round(start.Z);
        int endHeightX = (int)MathF.Round(end.X);
        int endHeightY = (int)MathF.Round(end.Z);

        float startMaskX = startHeightX * 0.5f;
        float startMaskY = startHeightY * 0.5f;
        float endMaskX = endHeightX * 0.5f;
        float endMaskY = endHeightY * 0.5f;

        float dx = endMaskX - startMaskX;
        float dy = endMaskY - startMaskY;
        float segmentLength = MathF.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)MathF.Ceiling(segmentLength));
        bool changed = false;

        minHeightX = Math.Min(minHeightX, Math.Min(startHeightX, endHeightX));
        minHeightY = Math.Min(minHeightY, Math.Min(startHeightY, endHeightY));
        maxHeightX = Math.Max(maxHeightX, Math.Max(startHeightX, endHeightX));
        maxHeightY = Math.Max(maxHeightY, Math.Max(startHeightY, endHeightY));

        for (int step = 0; step <= steps; step++)
        {
            float t = steps == 0 ? 0.0f : step / (float)steps;
            float centerX = Lerp(startMaskX, endMaskX, t);
            float centerY = Lerp(startMaskY, endMaskY, t);
            changed |= StampDisc(mask, centerX, centerY, riverValue, halfResRadius, ceilRadius);
        }

        return changed;
    }

    private static float Lerp(float start, float end, float t)
    {
        return start + (end - start) * t;
    }

    private static bool StampDisc(RiverMask mask, float centerX, float centerY, byte riverValue, float radius, int ceilRadius)
    {
        int centerXi = (int)MathF.Round(centerX);
        int centerYi = (int)MathF.Round(centerY);
        bool changed = false;

        for (int dz = -ceilRadius; dz <= ceilRadius; dz++)
        {
            for (int dx = -ceilRadius; dx <= ceilRadius; dx++)
            {
                int x = centerXi + dx;
                int y = centerYi + dz;
                if (x < 0 || x >= mask.Width || y < 0 || y >= mask.Height)
                    continue;

                float sampleX = x - centerX;
                float sampleY = y - centerY;
                float distance = MathF.Sqrt(sampleX * sampleX + sampleY * sampleY);
                if (distance > radius)
                    continue;

                if (mask.GetValue(x, y) == riverValue)
                    continue;

                mask.SetValue(x, y, riverValue);
                changed = true;
            }
        }

        return changed;
    }
}
