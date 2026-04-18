#nullable enable

using System;
using Stride.Core.Mathematics;

namespace Terrain.Editor.Services;

/// <summary>
/// Applies climate ids to the authoring mask. The generated material map is then
/// rebuilt from the edited mask rather than being painted directly.
/// </summary>
public sealed class ClimateEditor
{
    private static readonly Lazy<ClimateEditor> InstanceFactory = new(() => new());

    public static ClimateEditor Instance => InstanceFactory.Value;

    private ClimateEditor()
    {
    }

    public void ApplyStroke(Vector3 worldPosition, ClimateMask mask, TerrainManager terrainManager, byte climateId)
    {
        int pixelX = (int)MathF.Round(worldPosition.X) / 2;
        int pixelY = (int)MathF.Round(worldPosition.Z) / 2;

        var brush = BrushParameters.Instance;
        float radius = MathF.Ceiling(brush.Size * 0.5f) / 2.0f;
        float innerRadius = radius * brush.EffectiveFalloff;
        int ceilRadius = (int)MathF.Ceiling(radius);

        for (int dz = -ceilRadius; dz <= ceilRadius; dz++)
        {
            for (int dx = -ceilRadius; dx <= ceilRadius; dx++)
            {
                int x = pixelX + dx;
                int y = pixelY + dz;
                if (x < 0 || x >= mask.Width || y < 0 || y >= mask.Height)
                    continue;

                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > radius)
                    continue;

                float strength = PaintEditor.ComputeLinearFalloff(distance, radius, innerRadius) * brush.Strength;
                if (strength <= 0.0f)
                    continue;

                mask.SetValue(x, y, climateId);
            }
        }

        // Regenerate only the touched region so the climate workflow stays responsive.
        terrainManager.RegenerateMaterialIndices(pixelX, pixelY, radius);
    }
}
