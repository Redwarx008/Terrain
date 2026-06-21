#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering.River;

namespace Terrain.Editor.Services;

public sealed class RiverMeshService
{
    private const float StraightSampleSpacing = 1.0f;
    private const float ModerateCurveSampleSpacing = 0.5f;
    private const float TightCurveSampleSpacing = 0.25f;
    private const float ModerateCurveAngleDegrees = 4.0f;
    private const float TightCurveAngleDegrees = 10.0f;
    private const float CenterlineSimplificationTolerance = 1.5f;
    private const int CenterlineSmoothingIterations = 2;
    private const float BendRelaxationWeight = 0.4f;
    private const float ConnectionTaperDistance = 6.0f;
    private const float SurfaceOffset = 0.02f;
    private const float MinVisibleHalfWidth = 0.05f;
    private const float MinGeometricTaperScale = 0.75f;
    private const float RiverUvScale = 0.8f;
    private const float TerrainWorldToRiverMapUnits = 0.5f;

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

            var interpolatedPoints = CatmullRomInterpolate(smoothedPoints);
            seg.Centerline = ResampleTerrainHeights(interpolatedPoints);
            seg.WorldLength = ComputeMapUnitPolylineLength(seg.Centerline);
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

        if (iterations >= 2)
            current = RelaxRepeatedBends(current, iterations + 3);

        return current;
    }

    private static List<Vector3> RelaxRepeatedBends(List<Vector3> points, int passes)
    {
        if (points.Count <= 2 || passes <= 0)
            return points;

        var current = new List<Vector3>(points);
        for (int pass = 0; pass < passes; pass++)
        {
            var next = new List<Vector3>(current.Count) { current[0] };
            for (int i = 1; i < current.Count - 1; i++)
            {
                Vector3 relaxed = current[i] * (1.0f - BendRelaxationWeight * 2.0f)
                    + (current[i - 1] + current[i + 1]) * BendRelaxationWeight;
                next.Add(relaxed);
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

    private static List<Vector3> CatmullRomInterpolate(List<Vector3> controlPoints)
    {
        if (controlPoints.Count < 2) return new List<Vector3>(controlPoints);

        var result = new List<Vector3> { controlPoints[0] };
        float accumulated = 0.0f;

        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            Vector3 p0 = controlPoints[Math.Max(0, i - 1)];
            Vector3 p1 = controlPoints[i];
            Vector3 p2 = controlPoints[i + 1];
            Vector3 p3 = controlPoints[Math.Min(controlPoints.Count - 1, i + 2)];

            float spacing = ComputeAdaptiveSampleSpacing(p0, p1, p2, p3);
            float segmentLength = HorizontalDistance(p1, p2);
            if (segmentLength < 0.001f) continue;

            int steps = Math.Max(1, (int)(segmentLength / spacing));
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 point = CatmullRom(p0, p1, p2, p3, t);
                float dist = HorizontalDistance(result[^1], point);
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

    private List<Vector3> ResampleTerrainHeights(List<Vector3> points)
    {
        var result = new List<Vector3>(points.Count);
        foreach (Vector3 point in points)
            result.Add(new Vector3(point.X, SampleTerrainHeight(point.X, point.Z) + SurfaceOffset, point.Z));

        return result;
    }

    private static float ComputeAdaptiveSampleSpacing(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float maxTurnAngle = MathF.Max(
            ComputeTurnAngleDegrees(p0, p1, p2),
            ComputeTurnAngleDegrees(p1, p2, p3));

        if (maxTurnAngle >= TightCurveAngleDegrees)
            return TightCurveSampleSpacing;

        if (maxTurnAngle >= ModerateCurveAngleDegrees)
            return ModerateCurveSampleSpacing;

        return StraightSampleSpacing;
    }

    private static float ComputeTurnAngleDegrees(Vector3 previous, Vector3 current, Vector3 next)
    {
        Vector3 incoming = current - previous;
        Vector3 outgoing = next - current;
        incoming.Y = 0.0f;
        outgoing.Y = 0.0f;

        if (incoming.LengthSquared() <= 0.000001f || outgoing.LengthSquared() <= 0.000001f)
            return 0.0f;

        incoming.Normalize();
        outgoing.Normalize();
        float dot = Math.Clamp(Vector3.Dot(incoming, outgoing), -1.0f, 1.0f);
        return MathF.Acos(dot) * 180.0f / MathF.PI;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
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
        if (w <= 0 || h <= 0 || data.Length < w * h)
            return 0.0f;

        // World coordinates are 1:1 with heightmap pixels (0 ~ w-1).
        float x = Math.Clamp(wx, 0.0f, w - 1);
        float y = Math.Clamp(wz, 0.0f, h - 1);
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = Math.Min(x0 + 1, w - 1);
        int y1 = Math.Min(y0 + 1, h - 1);
        float tx = x - x0;
        float ty = y - y0;

        float h00 = data[y0 * w + x0] * (1.0f / ushort.MaxValue) * scale;
        float h10 = data[y0 * w + x1] * (1.0f / ushort.MaxValue) * scale;
        float h01 = data[y1 * w + x0] * (1.0f / ushort.MaxValue) * scale;
        float h11 = data[y1 * w + x1] * (1.0f / ushort.MaxValue) * scale;
        float hx0 = h00 + (h10 - h00) * tx;
        float hx1 = h01 + (h11 - h01) * tx;
        return hx0 + (hx1 - hx0) * ty;
    }

    // Task 5 methods below

    public (VertexPositionNormalTexture[] Vertices, int[] Indices) BuildRibbonMesh(RiverSegment segment, float widthScale)
    {
        var mesh = BuildRiverMesh(segment, widthScale);
        if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
            return (Array.Empty<VertexPositionNormalTexture>(), Array.Empty<int>());

        var vertices = new VertexPositionNormalTexture[mesh.Vertices.Length];
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            var riverVertex = mesh.Vertices[i];
            vertices[i] = new VertexPositionNormalTexture(riverVertex.Position.XYZ(), riverVertex.Normal, riverVertex.UV);
        }

        return (vertices, (int[])mesh.Indices.Clone());
    }

    public RiverMeshData BuildRiverMesh(RiverSegment segment, float widthScale)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var centerline = segment.Centerline;
        if (centerline == null || centerline.Count < 2)
            return new RiverMeshData { SegmentId = segment.SystemId };

        var mapUnitDistances = ComputeMapUnitDistances(centerline);
        float totalLength = mapUnitDistances.Length > 0 ? mapUnitDistances[^1] : 0.0f;
        float baseHalfWidth = Math.Max(MinVisibleHalfWidth, segment.AvgHalfWidth * widthScale);
        Vector2 mapWorldSize = GetMapWorldSize();
        float mapExtent = GetMapExtent(mapWorldSize);

        int n = centerline.Count;
        var vertices = new List<RiverVertex>(n * 2 + 2);
        var indices = new List<int>();

        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);

        for (int i = 0; i < n; i++)
        {
            Vector3 center = centerline[i];
            float mapUnitDistance = mapUnitDistances[i];
            float normalizedProgress = totalLength > 0.001f ? mapUnitDistance / totalLength : 0;
            float longitudinalUv = mapUnitDistance * RiverUvScale;
            float taperScale = ComputeTaperScale(normalizedProgress, totalLength, segment.TaperStart, segment.TaperEnd);
            float halfWidth = baseHalfWidth * taperScale;
            float normalizedWidth = NormalizeRiverWidth(halfWidth, mapExtent);
            Vector3 offset = ComputeMiterOffset(centerline, i, halfWidth);
            Vector3 tangent = EstimateCenterlineTangent(centerline, i);
            Vector3 normal = ComputeRibbonNormal(tangent, offset);
            float distanceToMain = ComputeDistanceToMain(normalizedProgress, segment.TaperStart, segment.TaperEnd);

            Vector3 leftPos = center - offset;
            Vector3 rightPos = center + offset;

            vertices.Add(new RiverVertex(leftPos, 1.0f, new Vector2(longitudinalUv, 0), tangent, normal, normalizedWidth, distanceToMain));
            vertices.Add(new RiverVertex(rightPos, 1.0f, new Vector2(longitudinalUv, 1), tangent, normal, normalizedWidth, distanceToMain));

            boundsMin = Vector3.Min(boundsMin, leftPos);
            boundsMin = Vector3.Min(boundsMin, rightPos);
            boundsMax = Vector3.Max(boundsMax, leftPos);
            boundsMax = Vector3.Max(boundsMax, rightPos);
        }

        // The draw data uses interleaved left/right boundary vertices along the river.
        // Emit the same organization as a triangle list with Stride-visible winding.
        for (int i = 0; i < n - 1; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;
            indices.Add(a); indices.Add(c); indices.Add(b);
            indices.Add(b); indices.Add(c); indices.Add(d);
        }

        var boundingBox = new BoundingBox(boundsMin, boundsMax);
        return new RiverMeshData
        {
            SegmentId = segment.SystemId,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            BoundingBox = boundingBox,
            BoundingSphere = BoundingSphere.FromBox(boundingBox),
            WorldLength = totalLength,
            AvgHalfWidth = segment.AvgHalfWidth,
            MapExtent = mapExtent,
            MapWorldSize = mapWorldSize,
        };
    }

    private static float[] ComputeMapUnitDistances(List<Vector3> points)
    {
        var distances = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
        {
            Vector2 previous = ToRiverMapUnits(points[i - 1]);
            Vector2 current = ToRiverMapUnits(points[i]);
            distances[i] = distances[i - 1] + Vector2.Distance(previous, current);
        }
        return distances;
    }

    private static float ComputeMapUnitPolylineLength(List<Vector3> points)
    {
        if (points.Count < 2)
            return 0.0f;

        float length = 0.0f;
        for (int i = 1; i < points.Count; i++)
        {
            length += Vector2.Distance(ToRiverMapUnits(points[i - 1]), ToRiverMapUnits(points[i]));
        }

        return length;
    }

    private static Vector2 ToRiverMapUnits(Vector3 point) =>
        new(point.X * TerrainWorldToRiverMapUnits, point.Z * TerrainWorldToRiverMapUnits);

    private Vector2 GetMapWorldSize()
    {
        if (terrainManager != null && terrainManager.HeightCacheWidth > 0 && terrainManager.HeightCacheHeight > 0)
        {
            return new Vector2(
                Math.Max(terrainManager.HeightCacheWidth - 1, 0) * TerrainWorldToRiverMapUnits,
                Math.Max(terrainManager.HeightCacheHeight - 1, 0) * TerrainWorldToRiverMapUnits);
        }

        return new Vector2(4096.0f, 4096.0f);
    }

    private static float GetMapExtent(Vector2 mapWorldSize)
    {
        return MathF.Max(mapWorldSize.X, mapWorldSize.Y);
    }

    private static float NormalizeRiverWidth(float halfWidth, float mapExtent)
    {
        return MathF.Max(halfWidth, 0.0f) / MathF.Max(mapExtent, 1.0f);
    }

    private static Vector3 EstimateCenterlineTangent(List<Vector3> centerline, int index)
    {
        if (centerline.Count < 2) return Vector3.UnitX;

        int previous = Math.Max(0, index - 1);
        int next = Math.Min(centerline.Count - 1, index + 1);
        Vector3 tangent = centerline[next] - centerline[previous];
        return tangent.LengthSquared() > 0.000001f ? Vector3.Normalize(tangent) : Vector3.UnitX;
    }

    private static Vector3 ComputeRibbonNormal(Vector3 tangent, Vector3 offset)
    {
        Vector3 side;
        if (offset.LengthSquared() > 0.000001f)
        {
            side = Vector3.Normalize(offset);
        }
        else
        {
            Vector3 horizontalTangent = HorizontalDirection(tangent);
            if (horizontalTangent.LengthSquared() <= 0.000001f)
                return Vector3.UnitY;

            side = Side(horizontalTangent);
        }
        Vector3 normal = Vector3.Cross(side, tangent);

        if (normal.LengthSquared() <= 0.000001f)
            return Vector3.UnitY;

        normal = Vector3.Normalize(normal);
        return normal.Y >= 0.0f ? normal : -normal;
    }

    private static float ComputeDistanceToMain(float u, bool taperStart, bool taperEnd)
    {
        float value = 1.0f;
        if (taperStart)
            value = Math.Min(value, Math.Clamp(u * 10.0f, 0.0f, 1.0f));
        if (taperEnd)
            value = Math.Min(value, Math.Clamp((1.0f - u) * 10.0f, 0.0f, 1.0f));
        return value;
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
        return Math.Max(MinGeometricTaperScale, scale);
    }

    private static float SmoothStep(float t) => t * t * (3.0f - 2.0f * t);

}
