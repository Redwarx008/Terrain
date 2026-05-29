#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

public sealed class RiverMeshService
{
    private const float CurveSampleSpacing = 2.0f;
    private const float ConnectionTaperDistance = 6.0f;
    private const float SurfaceOffset = 0.02f;
    private const float MinVisibleHalfWidth = 0.05f;

    private readonly TerrainManager terrainManager;

    public RiverMeshService(TerrainManager terrainManager)
    {
        this.terrainManager = terrainManager ?? throw new ArgumentNullException(nameof(terrainManager));
    }

    public void BuildCenterlines(List<RiverSegment> segments, int mapWidth, int mapHeight)
    {
        // river.png is 1/2 the resolution of the heightmap, and each heightmap pixel = 1 world unit.
        // So 1 river pixel = 2 world units.
        float pixelToWorld = 2.0f;

        foreach (var seg in segments)
        {
            if (seg.Cells.Count < 2) continue;

            // Build raw centerline from pixel centers
            var rawPoints = new List<Vector3>();
            foreach (var (x, y) in seg.Cells)
            {
                float wx = (x + 0.5f) * pixelToWorld;
                float wz = (y + 0.5f) * pixelToWorld;
                float wy = SampleTerrainHeight(wx, wz) + SurfaceOffset;
                rawPoints.Add(new Vector3(wx, wy, wz));
            }

            // Catmull-Rom interpolation
            seg.Centerline = CatmullRomInterpolate(rawPoints, CurveSampleSpacing);
            seg.WorldLength = ComputePolylineLength(seg.Centerline);
        }
    }

    private static List<Vector3> CatmullRomInterpolate(List<Vector3> controlPoints, float spacing)
    {
        if (controlPoints.Count < 2) return new List<Vector3>(controlPoints);

        var result = new List<Vector3> { controlPoints[0] };
        float accumulated = 0;

        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            Vector3 p0 = controlPoints[Math.Max(0, i - 1)];
            Vector3 p1 = controlPoints[i];
            Vector3 p2 = controlPoints[i + 1];
            Vector3 p3 = controlPoints[Math.Min(controlPoints.Count - 1, i + 2)];

            float segmentLength = Vector3.Distance(p1, p2);
            if (segmentLength < 0.001f) continue;

            int steps = Math.Max(1, (int)(segmentLength / spacing));
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 point = CatmullRom(p0, p1, p2, p3, t);
                float dist = Vector3.Distance(result[^1], point);
                accumulated += dist;
                if (accumulated >= spacing)
                {
                    result.Add(point);
                    accumulated = 0;
                }
            }
        }

        if (Vector3.Distance(result[^1], controlPoints[^1]) > 0.01f)
            result.Add(controlPoints[^1]);

        return result;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static float ComputePolylineLength(List<Vector3> points)
    {
        float len = 0;
        for (int i = 1; i < points.Count; i++)
            len += Vector3.Distance(points[i - 1], points[i]);
        return len;
    }

    private float SampleTerrainHeight(float wx, float wz)
    {
        if (!terrainManager.HasHeightCache) return 0;
        float scale = terrainManager.HeightScale;
        var data = terrainManager.HeightDataCache;
        if (data == null) return 0;
        int w = terrainManager.HeightCacheWidth;
        int h = terrainManager.HeightCacheHeight;

        // World coordinates are 1:1 with heightmap pixels (0 ~ w-1)
        int ix = Math.Clamp((int)Math.Round(wx), 0, w - 1);
        int iy = Math.Clamp((int)Math.Round(wz), 0, h - 1);
        return data[iy * w + ix] * (1.0f / ushort.MaxValue) * scale;
    }

    // Task 5 methods below

    public (VertexPositionNormalTexture[] Vertices, int[] Indices) BuildRibbonMesh(RiverSegment segment, float widthScale)
    {
        var centerline = segment.Centerline;
        if (centerline == null || centerline.Count < 2)
            return (Array.Empty<VertexPositionNormalTexture>(), Array.Empty<int>());

        float totalLength = segment.WorldLength;
        float baseHalfWidth = Math.Max(MinVisibleHalfWidth, segment.AvgHalfWidth * widthScale);

        int n = centerline.Count;
        var vertices = new List<VertexPositionNormalTexture>(n * 2);
        var indices = new List<int>(n * 6);

        float accumulated = 0;
        for (int i = 0; i < n; i++)
        {
            Vector3 tangent;
            if (i < n - 1)
                tangent = Vector3.Normalize(new Vector3(
                    centerline[i + 1].X - centerline[i].X, 0,
                    centerline[i + 1].Z - centerline[i].Z));
            else
                tangent = Vector3.Normalize(new Vector3(
                    centerline[i].X - centerline[i - 1].X, 0,
                    centerline[i].Z - centerline[i - 1].Z));

            if (tangent.Length() < 0.001f) tangent = new Vector3(1, 0, 0);

            Vector3 side = Vector3.Normalize(new Vector3(-tangent.Z, 0, tangent.X));

            float u = i > 0 ? accumulated / totalLength : 0;
            float taperScale = ComputeTaperScale(u, totalLength, segment.TaperStart, segment.TaperEnd);
            float halfWidth = baseHalfWidth * taperScale;

            Vector3 center = centerline[i];
            Vector3 leftPos = center - side * halfWidth;
            Vector3 rightPos = center + side * halfWidth;

            Vector3 normal = SampleTerrainNormal(center.X, center.Z);

            vertices.Add(new VertexPositionNormalTexture(leftPos, normal, new Vector2(u, 0)));
            vertices.Add(new VertexPositionNormalTexture(rightPos, normal, new Vector2(u, 1)));

            if (i > 0)
            {
                int a = (i - 1) * 2;
                int b = a + 1;
                int c = i * 2;
                int d = c + 1;
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }

            if (i < n - 1)
                accumulated += Vector3.Distance(centerline[i], centerline[i + 1]);
        }

        return (vertices.ToArray(), indices.ToArray());
    }

    private static float ComputeTaperScale(float u, float totalLength, bool taperStart, bool taperEnd)
    {
        float scale = 1.0f;
        float taperU = Math.Min(1.0f, ConnectionTaperDistance / Math.Max(totalLength, 1.0f));
        if (taperStart && u < taperU)
            scale = Math.Min(scale, SmoothStep(u / taperU));
        if (taperEnd && u > 1.0f - taperU)
            scale = Math.Min(scale, SmoothStep((1.0f - u) / taperU));
        return scale;
    }

    private static float SmoothStep(float t) => t * t * (3.0f - 2.0f * t);

    private Vector3 SampleTerrainNormal(float wx, float wz)
    {
        float h = 0.5f;
        float cx = SampleTerrainHeight(wx, wz);
        float dx = SampleTerrainHeight(wx + h, wz);
        float dz = SampleTerrainHeight(wx, wz + h);
        var n = Vector3.Normalize(new Vector3(cx - dx, h, cx - dz));
        return n.LengthSquared() > 0 ? n : new Vector3(0, 1, 0);
    }
}
