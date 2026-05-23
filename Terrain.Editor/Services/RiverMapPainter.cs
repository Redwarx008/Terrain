#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

/// <summary>
/// 绘制河流地图像素。替代旧版 RiverMaskWriter。
/// Channel 工具：1px Bresenham 线，每像素 (River, widthByte)。
/// 特殊像素：单击放置单像素 (Source/Confluence/Bifurcation/Ocean)。
/// Eraser：单像素擦除或 Disc 擦除。
/// </summary>
public sealed class RiverMapPainter
{
    private const float MaskResolutionRatio = 2.0f;
    private static readonly Lazy<RiverMapPainter> InstanceFactory = new(() => new RiverMapPainter());

    public static RiverMapPainter Instance => InstanceFactory.Value;

    private RiverMapPainter() { }

    /// <summary>沿笔触画 1px Bresenham 线，每像素写 (River, widthByte)</summary>
    public bool ApplyStroke(IReadOnlyList<Vector3> worldPoints, float worldWidth, RiverMap map, TerrainManager terrainManager, byte riverValue = 12, bool markProjectDirty = true)
    {
        if (worldPoints.Count == 0) return false;

        byte widthByte = Math.Min(riverValue, (byte)12);
        bool changed = false;

        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 start = worldPoints[i];
            Vector3 end = i + 1 < worldPoints.Count ? worldPoints[i + 1] : worldPoints[i];
            changed |= RasterizeLine(start, end, map, widthByte);
        }

        if (!changed) return false;
        terrainManager.MarkRiverMaskDirty(markProjectDirty);
        return true;
    }

    /// <summary>单击放置特殊像素 (Source/Confluence/Bifurcation/Ocean)</summary>
    public bool PlaceSpecialPixel(Vector3 worldPosition, RiverPixelType type, RiverMap map, TerrainManager terrainManager, bool markProjectDirty = true)
    {
        int mx = (int)MathF.Round(worldPosition.X * 0.5f);
        int my = (int)MathF.Round(worldPosition.Z * 0.5f);

        if ((uint)mx >= (uint)map.Width || (uint)my >= (uint)map.Height)
            return false;

        map.SetPixel(mx, my, type);
        terrainManager.MarkRiverMaskDirty(markProjectDirty);
        return true;
    }

    /// <summary>擦除：单像素或 Disc 区域擦除为 Land</summary>
    public bool ApplyEraser(IReadOnlyList<Vector3> worldPoints, float radius, RiverMap map, TerrainManager terrainManager, bool markProjectDirty = true)
    {
        if (worldPoints.Count == 0) return false;

        bool changed = false;
        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 start = worldPoints[i];
            Vector3 end = i + 1 < worldPoints.Count ? worldPoints[i + 1] : worldPoints[i];
            changed |= RasterizeEraserDisc(start, end, map, radius);
        }

        if (!changed) return false;
        terrainManager.MarkRiverMaskDirty(markProjectDirty);
        return true;
    }

    /// <summary>Bresenham 1px 线栅格化</summary>
    private static bool RasterizeLine(Vector3 start, Vector3 end, RiverMap map, byte widthByte)
    {
        int x0 = (int)MathF.Round(start.X * 0.5f);
        int y0 = (int)MathF.Round(start.Z * 0.5f);
        int x1 = (int)MathF.Round(end.X * 0.5f);
        int y1 = (int)MathF.Round(end.Z * 0.5f);

        bool changed = false;
        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            if ((uint)x0 < (uint)map.Width && (uint)y0 < (uint)map.Height)
            {
                RiverPixelType currentType = map.GetType(x0, y0);
                // 不覆盖特殊像素 (Source/Confluence/Bifurcation/Ocean)
                if (currentType is RiverPixelType.Source or RiverPixelType.Confluence
                    or RiverPixelType.Bifurcation or RiverPixelType.Ocean)
                    continue;
                if (currentType != RiverPixelType.River || map.GetWidth(x0, y0) != widthByte)
                {
                    map.SetPixel(x0, y0, RiverPixelType.River, widthByte);
                    changed = true;
                }
            }

            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }

        return changed;
    }

    /// <summary>Erase disc：圆形区域擦除为 Land</summary>
    private static bool RasterizeEraserDisc(Vector3 start, Vector3 end, RiverMap map, float radius)
    {
        float halfResRadius = radius * 0.5f / MaskResolutionRatio;
        int ceilRadius = (int)MathF.Ceiling(halfResRadius);

        int startMx = (int)MathF.Round(start.X * 0.5f);
        int startMy = (int)MathF.Round(start.Z * 0.5f);
        int endMx = (int)MathF.Round(end.X * 0.5f);
        int endMy = (int)MathF.Round(end.Z * 0.5f);

        float dx = endMx - startMx;
        float dy = endMy - startMy;
        float segmentLength = MathF.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)MathF.Ceiling(segmentLength));
        bool changed = false;

        for (int step = 0; step <= steps; step++)
        {
            float t = steps == 0 ? 0.0f : step / (float)steps;
            float cx = startMx + dx * t;
            float cy = startMy + dy * t;
            changed |= StampEraserDisc(map, cx, cy, halfResRadius, ceilRadius);
        }

        return changed;
    }

    private static bool StampEraserDisc(RiverMap map, float centerX, float centerY, float radius, int ceilRadius)
    {
        int cxi = (int)MathF.Round(centerX);
        int cyi = (int)MathF.Round(centerY);
        bool changed = false;

        for (int dz = -ceilRadius; dz <= ceilRadius; dz++)
        {
            for (int dx = -ceilRadius; dx <= ceilRadius; dx++)
            {
                int x = cxi + dx;
                int y = cyi + dz;
                if ((uint)x >= (uint)map.Width || (uint)y >= (uint)map.Height)
                    continue;
                if (MathF.Sqrt(dx * dx + dz * dz) > radius)
                    continue;
                if (map.GetType(x, y) == RiverPixelType.Land)
                    continue;

                map.SetPixel(x, y, RiverPixelType.Land);
                changed = true;
            }
        }

        return changed;
    }
}
