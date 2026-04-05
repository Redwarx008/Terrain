#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// 材质擦除工具，将像素重置为默认材质索引 0。
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

                // 边界裁剪
                if (x < 0 || x >= context.DataWidth || z < 0 || z >= context.DataHeight)
                    continue;

                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > context.BrushRadius)
                    continue;

                // Falloff 控制强度
                float falloff = ComputeLinearFalloff(distance, context.BrushRadius, context.BrushInnerRadius);

                // 只有当 falloff 足够高时才擦除
                if (falloff * context.Strength > 0.5f)
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
