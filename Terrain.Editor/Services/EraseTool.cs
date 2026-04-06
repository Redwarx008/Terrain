#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Erase material index values back to the default slot (0).
/// </summary>
internal sealed class EraseTool : IPaintTool
{
    public string Name => "Erase";

    public void Apply(ref PaintEditContext context)
    {
        int radius = (int)MathF.Ceiling(context.BrushRadius);
        const byte defaultIndex = 0;

        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = context.CenterX + dx;
                int z = context.CenterZ + dz;

                if (x < 0 || x >= context.DataWidth || z < 0 || z >= context.DataHeight)
                    continue;

                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > context.BrushRadius)
                    continue;

                float falloff = ComputeLinearFalloff(distance, context.BrushRadius, context.BrushInnerRadius);

                // Discrete index painting: map strength to coverage threshold.
                float paintThreshold = 1.0f - context.Strength;
                if (falloff >= paintThreshold)
                {
                    context.IndexMap.SetIndex(x, z, defaultIndex);
                }
            }
        }
    }

    private static float ComputeLinearFalloff(float distance, float outerRadius, float innerRadius)
    {
        if (distance <= innerRadius)
            return 1.0f;

        if (distance >= outerRadius)
            return 0.0f;

        return 1.0f - (distance - innerRadius) / (outerRadius - innerRadius);
    }
}
