#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// 材质绘制工具，设置像素的材质索引为目标值。
/// 使用 falloff 控制边缘软硬。
/// </summary>
internal sealed class PaintMaterialTool : IPaintTool
{
    public string Name => "Paint";

    public void Apply(ref PaintEditContext context)
    {
        int radius = (int)MathF.Ceiling(context.BrushRadius);
        byte targetIndex = context.TargetMaterialIndex;

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

                // 只有当 falloff 足够高时才设置（避免边缘抖动）
                if (falloff * context.Strength > 0.5f)
                {
                    context.IndexMap.SetIndex(x, z, targetIndex);
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
