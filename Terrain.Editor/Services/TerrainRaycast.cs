#nullable enable
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Utility class for raycasting against terrain.
/// Provides screen-to-world ray conversion and terrain intersection.
/// </summary>
public static class TerrainRaycast
{
    /// <summary>
    /// Converts screen coordinates to a world-space ray using Stride's Viewport.Unproject.
    /// </summary>
    /// <param name="screenX">Mouse X in screen pixels</param>
    /// <param name="screenY">Mouse Y in screen pixels</param>
    /// <param name="viewportX">Viewport left edge in screen pixels</param>
    /// <param name="viewportY">Viewport top edge in screen pixels</param>
    /// <param name="viewportWidth">Viewport width in pixels</param>
    /// <param name="viewportHeight">Viewport height in pixels</param>
    /// <param name="camera">The camera component</param>
    /// <returns>Tuple of (ray origin, ray direction)</returns>
    public static (Vector3 Origin, Vector3 Direction) ScreenToWorldRay(
        float screenX,
        float screenY,
        float viewportX,
        float viewportY,
        float viewportWidth,
        float viewportHeight,
        CameraComponent camera)
    {
        // Create a Stride Viewport - this handles all the NDC conversion correctly
        var viewport = new Viewport(viewportX, viewportY, viewportWidth, viewportHeight);

        // Use Stride's Unproject to get near and far points
        // Z=0 for near plane, Z=1 for far plane
        var nearPoint = viewport.Unproject(
            new Vector3(screenX, screenY, 0.0f),
            camera.ProjectionMatrix,
            camera.ViewMatrix,
            Matrix.Identity);

        var farPoint = viewport.Unproject(
            new Vector3(screenX, screenY, 1.0f),
            camera.ProjectionMatrix,
            camera.ViewMatrix,
            Matrix.Identity);

        // Calculate ray direction
        var direction = Vector3.Normalize(farPoint - nearPoint);

        return (nearPoint, direction);
    }

    /// <summary>
    /// Calculates intersection of a ray with an infinite plane.
    /// </summary>
    public static float? RayPlaneIntersection(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        Vector3 planePoint,
        Vector3 planeNormal)
    {
        float denominator = Vector3.Dot(rayDirection, planeNormal);

        if (MathF.Abs(denominator) < 1e-6f)
            return null; // Ray is parallel to plane

        float t = Vector3.Dot(planePoint - rayOrigin, planeNormal) / denominator;
        return t >= 0 ? t : null;
    }

    /// <summary>
    /// Finds the intersection of a ray with the terrain surface.
    /// Uses iterative refinement to find the exact height.
    /// </summary>
    /// <param name="rayOrigin">Ray origin in world space</param>
    /// <param name="rayDirection">Ray direction (normalized)</param>
    /// <param name="terrainManager">Terrain manager with height data</param>
    /// <param name="maxIterations">Maximum refinement iterations</param>
    /// <returns>Intersection point on terrain surface, or null if no intersection</returns>
    public static Vector3? RayTerrainIntersection(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        TerrainManager terrainManager,
        int maxIterations = 20)
    {
        // Brush projection must stay stable on steep slopes, so use a bracketed search instead of
        // the previous Y-only Newton iteration which could skip the surface on aggressive gradients.
        if (rayDirection.Y >= 0)
            return null;

        const float maxDistance = 10000.0f;
        const float convergenceThreshold = 0.05f;
        const float sampleStep = 2.0f;

        float t = 0.0f;

        // Skip forward until the ray footprint enters terrain XZ bounds.
        for (int i = 0; i < 256 && t <= maxDistance; i++)
        {
            var testPoint = rayOrigin + rayDirection * t;
            if (terrainManager.IsPositionOnTerrain(testPoint.X, testPoint.Z))
                break;

            t += sampleStep;
        }

        if (t > maxDistance)
            return null;

        float? lastAboveT = null;
        float lastAboveHeight = 0.0f;

        for (int i = 0; i < 8192 && t <= maxDistance; i++, t += sampleStep)
        {
            var point = rayOrigin + rayDirection * t;
            if (!terrainManager.IsPositionOnTerrain(point.X, point.Z))
            {
                if (lastAboveT.HasValue)
                    break;

                continue;
            }

            float? terrainHeight = terrainManager.GetHeightAtPosition(point.X, point.Z);
            if (terrainHeight == null)
                continue;

            float heightDiff = point.Y - terrainHeight.Value;
            if (MathF.Abs(heightDiff) <= convergenceThreshold)
                return new Vector3(point.X, terrainHeight.Value, point.Z);

            if (heightDiff > 0.0f)
            {
                lastAboveT = t;
                lastAboveHeight = terrainHeight.Value;
                continue;
            }

            if (!lastAboveT.HasValue)
                continue;

            float minT = lastAboveT.Value;
            float maxT = t;
            float resolvedHeight = terrainHeight.Value;
            bool hasResolvedHeight = true;

            for (int refine = 0; refine < maxIterations; refine++)
            {
                float midT = (minT + maxT) * 0.5f;
                var midPoint = rayOrigin + rayDirection * midT;
                float? midHeight = terrainManager.GetHeightAtPosition(midPoint.X, midPoint.Z);
                if (midHeight == null)
                    return null;

                float midDiff = midPoint.Y - midHeight.Value;
                resolvedHeight = midHeight.Value;
                hasResolvedHeight = true;

                if (MathF.Abs(midDiff) <= convergenceThreshold)
                    return new Vector3(midPoint.X, midHeight.Value, midPoint.Z);

                if (midDiff > 0.0f)
                {
                    minT = midT;
                    lastAboveHeight = midHeight.Value;
                }
                else
                {
                    maxT = midT;
                }
            }

            float finalT = (minT + maxT) * 0.5f;
            var finalPoint = rayOrigin + rayDirection * finalT;
            float? finalHeight = terrainManager.GetHeightAtPosition(finalPoint.X, finalPoint.Z);
            if (finalHeight == null)
                finalHeight = hasResolvedHeight ? resolvedHeight : lastAboveHeight;

            return new Vector3(finalPoint.X, finalHeight.Value, finalPoint.Z);
        }

        return null;
    }
}
