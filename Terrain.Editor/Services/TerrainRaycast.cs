#nullable enable
using Stride.Core.Mathematics;
using Stride.Engine;
using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Utility class for raycasting against terrain.
/// Provides screen-to-world ray conversion and terrain intersection.
/// </summary>
public static class TerrainRaycast
{
    /// <summary>
    /// Converts screen coordinates to a world-space ray.
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
        // Convert to viewport-relative coordinates
        float x = screenX - viewportX;
        float y = screenY - viewportY;

        // Convert to NDC (-1 to 1)
        float ndcX = (2.0f * x / viewportWidth) - 1.0f;
        float ndcY = 1.0f - (2.0f * y / viewportHeight);

        // Get inverse view-projection matrix
        var viewProj = Matrix.Multiply(camera.ViewMatrix, camera.ProjectionMatrix);
        Matrix.Invert(ref viewProj, out var inverseViewProj);

        // Near point (z = 0 in NDC)
        var nearPoint = Vector4.Transform(new Vector4(ndcX, ndcY, 0.0f, 1.0f), inverseViewProj);
        nearPoint /= nearPoint.W;

        // Far point (z = 1 in NDC)
        var farPoint = Vector4.Transform(new Vector4(ndcX, ndcY, 1.0f, 1.0f), inverseViewProj);
        farPoint /= farPoint.W;

        // Calculate ray
        var origin = new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z);
        var direction = Vector3.Normalize(new Vector3(
            farPoint.X - nearPoint.X,
            farPoint.Y - nearPoint.Y,
            farPoint.Z - nearPoint.Z));

        return (origin, direction);
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
        // First, do a quick plane intersection at average terrain height
        var bounds = terrainManager.GetTerrainBounds();
        float avgHeight = (bounds.Minimum.Y + bounds.Maximum.Y) * 0.5f;

        float? tPlane = RayPlaneIntersection(
            rayOrigin,
            rayDirection,
            new Vector3(0, avgHeight, 0),
            Vector3.UnitY);

        if (tPlane == null)
            return null;

        // Start from plane intersection and refine
        float t = tPlane.Value;
        float step = 1.0f;

        for (int i = 0; i < maxIterations; i++)
        {
            var point = rayOrigin + rayDirection * t;

            // Check if point is within terrain bounds
            if (!terrainManager.IsPositionOnTerrain(point.X, point.Z))
            {
                // Move along ray to try to find terrain
                t += step;
                step *= 2.0f;

                // If we've gone too far, give up
                if (t > 10000f)
                    return null;

                continue;
            }

            // Get terrain height at this position
            float? terrainHeight = terrainManager.GetHeightAtPosition(point.X, point.Z);
            if (terrainHeight == null)
            {
                t += step;
                step *= 2.0f;
                continue;
            }

            float heightDiff = point.Y - terrainHeight.Value;

            // Converged to surface
            if (MathF.Abs(heightDiff) < 0.01f)
            {
                return new Vector3(point.X, terrainHeight.Value, point.Z);
            }

            // Adjust t based on height difference
            if (heightDiff > 0)
            {
                // We're above terrain, move forward
                t += heightDiff / MathF.Max(rayDirection.Y, 0.001f) * 0.5f;
            }
            else
            {
                // We're below terrain, move backward
                t += heightDiff / MathF.Max(rayDirection.Y, 0.001f) * 0.5f;
            }

            step *= 0.8f;
        }

        // Return best approximation
        var finalPoint = rayOrigin + rayDirection * t;
        float? finalHeight = terrainManager.GetHeightAtPosition(finalPoint.X, finalPoint.Z);
        if (finalHeight != null && terrainManager.IsPositionOnTerrain(finalPoint.X, finalPoint.Z))
        {
            return new Vector3(finalPoint.X, finalHeight.Value, finalPoint.Z);
        }

        return null;
    }
}
