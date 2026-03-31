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
        // If ray direction Y is near zero or positive, we can't reliably intersect terrain from above
        if (rayDirection.Y >= 0)
            return null;

        // Simple approach: walk along the ray and find where it crosses terrain surface
        // Start from ray origin and move forward

        float t = 0;
        float step = 1.0f;

        // First, find a point that is inside terrain bounds
        for (int i = 0; i < 100; i++)
        {
            var testPoint = rayOrigin + rayDirection * t;
            if (terrainManager.IsPositionOnTerrain(testPoint.X, testPoint.Z))
                break;
            t += step;
            step *= 2f;
            if (t > 10000f)
                return null;
        }

        // Now refine to find terrain surface
        for (int i = 0; i < maxIterations; i++)
        {
            var point = rayOrigin + rayDirection * t;

            // Check if point is within terrain bounds
            if (!terrainManager.IsPositionOnTerrain(point.X, point.Z))
            {
                return null;
            }

            // Get terrain height at this position
            float? terrainHeight = terrainManager.GetHeightAtPosition(point.X, point.Z);
            if (terrainHeight == null)
            {
                return null;
            }

            float heightDiff = point.Y - terrainHeight.Value;

            // Converged to surface
            if (MathF.Abs(heightDiff) < 0.1f)
            {
                return new Vector3(point.X, terrainHeight.Value, point.Z);
            }

            // Adjust t: since rayDirection.Y < 0 (pointing down)
            // If heightDiff > 0 (point above terrain), we need to go forward (increase t)
            // If heightDiff < 0 (point below terrain), we need to go backward (decrease t)
            // The adjustment is: t -= heightDiff / rayDirection.Y
            // Since rayDirection.Y < 0:
            //   - heightDiff > 0 => adjustment is positive => t increases => go forward
            //   - heightDiff < 0 => adjustment is negative => t decreases => go backward
            t -= heightDiff / rayDirection.Y;
        }

        // Return best approximation
        var finalPoint = rayOrigin + rayDirection * t;
        float? finalHeight = terrainManager.GetHeightAtPosition(finalPoint.X, finalPoint.Z);
        if (finalHeight != null && terrainManager.IsPositionOnTerrain(finalPoint.X, finalPoint.Z))
        {
            float finalHeightDiff = MathF.Abs(finalPoint.Y - finalHeight.Value);
            if (finalHeightDiff < 1.0f) // Accept if within 1 meter
            {
                return new Vector3(finalPoint.X, finalHeight.Value, finalPoint.Z);
            }
        }

        return null;
    }
}
