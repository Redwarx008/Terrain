#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Editor.Services.Commands;

namespace Terrain.Editor.Services;

/// <summary>
/// Applies biome ids to the authoring mask. GPU splatmap generation then consumes
/// the edited mask directly instead of any CPU-side material cache.
/// </summary>
public sealed class BiomeEditor
{
    private static readonly Lazy<BiomeEditor> InstanceFactory = new(() => new());

    public static BiomeEditor Instance => InstanceFactory.Value;

    private BiomeEditor()
    {
    }

    public void BeginStroke(TerrainManager terrainManager, byte biomeId)
    {
        var command = new BiomeEditCommand(terrainManager, biomeId);
        HistoryManager.Instance.BeginCommand(command);
    }

    public void ApplyStroke(Vector3 worldPosition, BiomeMask mask, TerrainManager terrainManager, byte biomeId)
    {
        var brush = BrushParameters.Instance;
        if (brush.Strength <= 0.0f)
            return;

        // 先在 heightmap 空间确定笔刷中心，再转换到 1/2 分辨率的 biome/splat 空间。
        int heightmapX = (int)MathF.Round(worldPosition.X);
        int heightmapY = (int)MathF.Round(worldPosition.Z);
        int maskX = heightmapX / 2;
        int maskY = heightmapY / 2;

        float heightmapRadius = brush.Size * 0.5f;
        float halfResRadius = heightmapRadius / 2.0f;
        float innerRadius = halfResRadius * brush.EffectiveFalloff;
        int ceilRadius = (int)MathF.Ceiling(halfResRadius);

        // 先标记 chunk，再写入数据 — 确保捕获到真实的 before-state。
        // 注意：BiomeMask 是 1/2 分辨率，必须用 mask 空间坐标匹配 GetDataWidth/Height。
        HistoryManager.Instance.MarkCommandChunks(maskX, maskY, halfResRadius);

        bool changed = false;

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

                // 单通道 BiomeMask 只能存离散 ID。对 biome 分类绘制来说，
                // 只要落在笔刷覆盖范围内就应稳定写入目标 biome，而不是随机缺块。
                float strength = ComputeLinearFalloff(distance, halfResRadius, innerRadius) * brush.Strength;
                if (strength > 0.0f)
                {
                    byte current = mask.GetValue(x, y);
                    if (current == biomeId)
                        continue;

                    mask.SetValue(x, y, biomeId);
                    changed = true;
                }
            }
        }

        if (!changed)
            return;

        terrainManager.MarkBiomeMaskDirty();
        terrainManager.RegenerateMaterialIndices(heightmapX, heightmapY, heightmapRadius);
    }

    public void EndStroke()
    {
        HistoryManager.Instance.CommitCommand();
    }

    public void CancelStroke()
    {
        HistoryManager.Instance.CancelCommand();
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
