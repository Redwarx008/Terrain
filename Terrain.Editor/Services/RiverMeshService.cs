#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

public sealed class RiverMeshService
{
    private const float CurveSampleSpacing = 1.0f;
    private const float CenterlineSimplificationTolerance = 1.5f;
    private const int CenterlineSmoothingIterations = 2;
    private const float ConnectionTaperDistance = 6.0f;
    private const float SurfaceOffset = 0.02f;
    private const float MinVisibleHalfWidth = 0.05f;

    private readonly TerrainManager? terrainManager;

    public RiverMeshService(TerrainManager? terrainManager)
    {
        this.terrainManager = terrainManager;
    }

    public void BuildCenterlines(List<RiverSegment> segments, int mapWidth, int mapHeight)
    {
        if (terrainManager == null)
            throw new InvalidOperationException("River centerline generation requires a TerrainManager for height sampling.");

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

            var simplifiedPoints = SimplifyCenterline(rawPoints, CenterlineSimplificationTolerance);
            var smoothedPoints = SmoothCenterline(simplifiedPoints, CenterlineSmoothingIterations);

            // Catmull-Rom interpolation
            seg.Centerline = CatmullRomInterpolate(smoothedPoints, CurveSampleSpacing);
            seg.WorldLength = ComputePolylineLength(seg.Centerline);
        }
    }

    internal static List<Vector3> SimplifyCenterline(List<Vector3> points, float tolerance)
    {
        if (points.Count <= 2 || tolerance <= 0)
            return new List<Vector3>(points);

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyCenterline(points, 0, points.Count - 1, tolerance * tolerance, keep);

        var result = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }
        return result;
    }

    internal static List<Vector3> SmoothCenterline(List<Vector3> points, int iterations)
    {
        if (points.Count <= 2 || iterations <= 0)
            return new List<Vector3>(points);

        var current = new List<Vector3>(points);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var next = new List<Vector3>(current.Count * 2);
            next.Add(current[0]);
            for (int i = 0; i < current.Count - 1; i++)
            {
                Vector3 a = current[i];
                Vector3 b = current[i + 1];
                next.Add(a * 0.75f + b * 0.25f);
                next.Add(a * 0.25f + b * 0.75f);
            }
            next.Add(current[^1]);
            current = next;
        }
        return current;
    }

    private static void SimplifyCenterline(List<Vector3> points, int start, int end, float toleranceSquared, bool[] keep)
    {
        if (end <= start + 1)
            return;

        float maxDistanceSquared = -1;
        int maxIndex = -1;
        for (int i = start + 1; i < end; i++)
        {
            float distanceSquared = DistanceToSegmentSquared(points[i], points[start], points[end]);
            if (distanceSquared > maxDistanceSquared)
            {
                maxDistanceSquared = distanceSquared;
                maxIndex = i;
            }
        }

        if (maxDistanceSquared > toleranceSquared && maxIndex >= 0)
        {
            keep[maxIndex] = true;
            SimplifyCenterline(points, start, maxIndex, toleranceSquared, keep);
            SimplifyCenterline(points, maxIndex, end, toleranceSquared, keep);
        }
    }

    private static float DistanceToSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        segment.Y = 0;
        Vector3 delta = point - start;
        delta.Y = 0;

        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.000001f)
            return delta.LengthSquared();

        float t = Math.Clamp(Vector3.Dot(delta, segment) / lengthSquared, 0, 1);
        Vector3 projected = start + segment * t;
        Vector3 distance = point - projected;
        distance.Y = 0;
        return distance.LengthSquared();
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
        if (terrainManager == null || !terrainManager.HasHeightCache) return 0;
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
        var vertices = new List<VertexPositionNormalTexture>(n * 2 + 2);
        var indices = new List<int>();

        var distances = ComputeDistances(centerline);
        for (int i = 0; i < n; i++)
        {
            Vector3 center = centerline[i];
            float u = totalLength > 0.001f ? distances[i] / totalLength : 0;
            float taperScale = ComputeTaperScale(u, totalLength, segment.TaperStart, segment.TaperEnd);
            float halfWidth = baseHalfWidth * taperScale;
            Vector3 offset = ComputeMiterOffset(centerline, i, halfWidth);

            Vector3 leftPos = center - offset;
            Vector3 rightPos = center + offset;
            Vector3 normal = SampleTerrainNormal(center.X, center.Z);

            vertices.Add(new VertexPositionNormalTexture(leftPos, normal, new Vector2(u, 0)));
            vertices.Add(new VertexPositionNormalTexture(rightPos, normal, new Vector2(u, 1)));
        }

        // CK3 Draw(580) uses a triangle-strip style organization: boundary vertices are
        // interleaved left/right along the river, with degenerate vertices at strip boundaries.
        // Stride's MeshDraw path here uses TriangleList, so emit the same interleaved strip
        // topology with the winding expected by Stride's current culling state.
        for (int i = 0; i < n - 1; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;
            indices.Add(a); indices.Add(c); indices.Add(b);
            indices.Add(b); indices.Add(c); indices.Add(d);
        }

        return (vertices.ToArray(), indices.ToArray());
    }

    private static float[] ComputeDistances(List<Vector3> points)
    {
        var distances = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
            distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        return distances;
    }

    private static Vector3 ComputeMiterOffset(List<Vector3> centerline, int index, float halfWidth)
    {
        Vector3 prev = index > 0 ? HorizontalDirection(centerline[index] - centerline[index - 1]) : Vector3.Zero;
        Vector3 next = index < centerline.Count - 1 ? HorizontalDirection(centerline[index + 1] - centerline[index]) : Vector3.Zero;

        if (prev.LengthSquared() <= 0.000001f && next.LengthSquared() <= 0.000001f)
            return new Vector3(0, 0, halfWidth);

        if (prev.LengthSquared() <= 0.000001f)
            return Side(next) * halfWidth;

        if (next.LengthSquared() <= 0.000001f)
            return Side(prev) * halfWidth;

        Vector3 miter = Side(prev) + Side(next);
        if (miter.LengthSquared() <= 0.000001f)
            return Side(next) * halfWidth;

        miter = Vector3.Normalize(miter);
        float denominator = MathF.Abs(Vector3.Dot(miter, Side(next)));
        float scale = denominator > 0.001f ? halfWidth / denominator : halfWidth;
        scale = MathF.Min(scale, halfWidth * 2.0f);
        return miter * scale;
    }

    private static Vector3 Side(Vector3 tangent) => Vector3.Normalize(new Vector3(-tangent.Z, 0, tangent.X));

    private static Vector3 HorizontalDirection(Vector3 value)
    {
        value.Y = 0;
        return value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : Vector3.Zero;
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
