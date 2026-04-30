#nullable enable

using System;
using Stride.Core.Mathematics;

namespace Terrain.Editor.Services;

/// <summary>
/// Applies biome ids to the authoring mask. The generated material map is then
/// rebuilt from the edited mask rather than being painted directly.
/// </summary>
public sealed class BiomeEditor
{
    private static readonly Lazy<BiomeEditor> InstanceFactory = new(() => new());

    public static BiomeEditor Instance => InstanceFactory.Value;

    private BiomeEditor()
    {
    }

    public void ApplyStroke(Vector3 worldPosition, BiomeMask mask, TerrainManager terrainManager, byte biomeId)
    {
        // BiomeMask 使用 1/2 分辨率，画笔坐标需从高度图空间转换
        int maskX = (int)MathF.Round(worldPosition.X / 2.0f);
        int maskY = (int)MathF.Round(worldPosition.Z / 2.0f);

        var brush = BrushParameters.Instance;
        float halfResRadius = MathF.Ceiling(brush.Size * 0.5f) / 2.0f;
        float innerRadius = halfResRadius * brush.EffectiveFalloff;
        int ceilRadius = (int)MathF.Ceiling(halfResRadius);

        for (int dz = -ceilRadius; dz <= ceilRadius; dz++)
        {
            for (int dx = -ceilRadius; dx <= ceilRadius; dx++)
            {
                int x = maskX + dx;
                int y = maskY + dz;
                if (x < 0 || x >= mask.Width || y < 0 || y >= mask.Height)
                    continue;

                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > halfResRadius)
                    continue;

                // Biome ID 为离散值，用强度控制覆盖概率实现软边缘混合
                float strength = ComputeLinearFalloff(distance, halfResRadius, innerRadius) * brush.Strength;
                if (strength > 0.0f)
                {
                    byte current = mask.GetValue(x, y);
                    if (current == biomeId)
                        continue;

                    // 按强度概率覆盖：强度越高越可能写入新 ID
                    if (Random.Shared.NextSingle() < strength)
                        mask.SetValue(x, y, biomeId);
                }
            }
        }

        terrainManager.MarkBiomeMaskDirty();
        // 脏标记使用高度图空间坐标（与 slice 相交判定需要全分辨率坐标）
        int heightmapX = (int)MathF.Round(worldPosition.X);
        int heightmapY = (int)MathF.Round(worldPosition.Z);
        float heightmapRadius = MathF.Ceiling(brush.Size * 0.5f);
        terrainManager.RegenerateMaterialIndices(heightmapX, heightmapY, heightmapRadius);
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
