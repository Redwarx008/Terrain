#nullable enable

using System;

namespace Terrain.Editor.Services;

/// <summary>
/// Shared paint logic for material index editing.
/// Keeps paint and erase behavior consistent by using the same weight-transition model.
/// </summary>
internal static class PaintBrushCore
{
    private const float MinSwitchWeight = 0.05f;
    private const float HeightNormalization = 1.0f / ushort.MaxValue;

    public static void Apply(ref PaintEditContext context, byte targetIndex)
    {
        int radius = (int)MathF.Ceiling(context.BrushRadius);
        float targetWeight = Math.Clamp(context.Weight, 0.0f, 1.0f);

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
                // Use a squared falloff to keep brush edge transitions closer to the reference shader feel.
                float brushStrength = 2.0f * context.Strength * falloff * falloff;

                // Apply slope filter: reduce strength outside the configured slope range
                brushStrength *= ComputeSlopeMultiplier(context, x, z);
                if (brushStrength <= 0.0f)
                    continue;

                MaterialPixel pixel = context.IndexMap.GetPixel(x, z);
                float weight = pixel.Weight / 255.0f;
                bool switchedToTarget = false;

                if (pixel.Index == targetIndex)
                {
                    weight = MathF.Min(1.0f, weight + brushStrength);
                }
                else
                {
                    weight -= brushStrength;
                    if (weight <= 0.0f)
                    {
                        switchedToTarget = true;
                        pixel.Index = targetIndex;
                        weight = MathF.Max(-weight, MathF.Max(MinSwitchWeight, targetWeight));
                        pixel.Rotation = ComputeRotationByte(context, x, z);
                        pixel.Projection = ComputeProjectionByte(context, x, z);
                    }
                }

                // Keep projection up-to-date while actively painting the same material in 3D mode.
                if (!switchedToTarget && pixel.Index == targetIndex && context.Use3DProjection)
                {
                    pixel.Projection = ComputeProjectionByte(context, x, z);
                }

                pixel.Weight = (byte)Math.Clamp((int)MathF.Round(Math.Clamp(weight, 0.0f, 1.0f) * 255.0f), 0, 255);
                context.IndexMap.SetPixel(x, z, pixel);
            }
        }
    }

    public static float ComputeLinearFalloff(float distance, float outerRadius, float innerRadius)
    {
        if (distance <= innerRadius)
            return 1.0f;

        if (distance >= outerRadius)
            return 0.0f;

        return 1.0f - (distance - innerRadius) / (outerRadius - innerRadius);
    }

    private static byte ComputeRotationByte(in PaintEditContext context, int x, int z)
    {
        if (!context.RandomRotation)
        {
            if (context.FixedRotationDegrees <= 0.0f)
                return 0;

            return (byte)Math.Clamp((int)MathF.Round(context.FixedRotationDegrees / 360.0f * 255.0f), 0, 255);
        }

        return HashToByte(x, z, context.RandomSeed);
    }

    private static byte ComputeProjectionByte(in PaintEditContext context, int x, int z)
    {
        if (!context.Use3DProjection || context.HeightData == null || context.HeightDataWidth <= 0 || context.HeightDataHeight <= 0)
            return 0x77;

        float left = SampleHeight(context.HeightData, context.HeightDataWidth, context.HeightDataHeight, x - 1, z);
        float right = SampleHeight(context.HeightData, context.HeightDataWidth, context.HeightDataHeight, x + 1, z);
        float up = SampleHeight(context.HeightData, context.HeightDataWidth, context.HeightDataHeight, x, z - 1);
        float down = SampleHeight(context.HeightData, context.HeightDataWidth, context.HeightDataHeight, x, z + 1);

        float nx = left - right;
        float nz = up - down;
        const float ny = 2.0f;
        return MaterialIndexMap.EncodeProjectionDirection(nx, ny, nz);
    }

    /// <summary>
    /// 计算坡度过滤乘数 (0-1)。
    /// 坡度在 [MinSlope, MaxSlope] 范围内返回 1.0，否则返回 0.0。
    /// 未启用或无高度数据时返回 1.0。
    /// 使用余弦空间比较，避免每像素调用 MathF.Acos。
    /// </summary>
    private static float ComputeSlopeMultiplier(in PaintEditContext context, int x, int z)
    {
        if (!context.UseSlopeFilter)
            return 1.0f;

        if (context.HeightData == null || context.HeightDataWidth <= 0 || context.HeightDataHeight <= 0 || context.HeightScale <= 0.0f)
            return 1.0f;

        // Splatmap → heightmap 坐标映射 (2:1 比率)
        int hx = x * 2;
        int hz = z * 2;

        float left = SampleHeight(context.HeightData, context.HeightDataWidth, context.HeightDataHeight, hx - 1, hz);
        float right = SampleHeight(context.HeightData, context.HeightDataWidth, context.HeightDataHeight, hx + 1, hz);
        float up = SampleHeight(context.HeightData, context.HeightDataWidth, context.HeightDataHeight, hx, hz - 1);
        float down = SampleHeight(context.HeightData, context.HeightDataWidth, context.HeightDataHeight, hx, hz + 1);

        // 世界空间法线：高度梯度乘以 HeightScale 转换为世界单位
        float worldNx = (left - right) * context.HeightScale;
        float worldNz = (up - down) * context.HeightScale;
        const float worldNy = 2.0f; // 2 个 heightmap 像素 = 2 世界单位水平距离

        float normalLength = MathF.Sqrt(worldNx * worldNx + worldNz * worldNz + worldNy * worldNy);
        float normalY = worldNy / normalLength; // cos(坡度角)

        // 在余弦空间中比较，避免 acos：
        // normalY = cos(slopeAngle)，cos 递减，所以 slope↑ ↔ normalY↓
        // slope >= minSlope ↔ normalY <= cos(minSlope)
        // slope <= maxSlope ↔ normalY >= cos(maxSlope)
        float cosMinSlope = MathF.Cos(context.MinSlopeDegrees * (MathF.PI / 180.0f));
        float cosMaxSlope = MathF.Cos(context.MaxSlopeDegrees * (MathF.PI / 180.0f));

        // 在范围内 → 绘制，范围外 → 不绘制
        return (normalY <= cosMinSlope && normalY >= cosMaxSlope) ? 1.0f : 0.0f;
    }

    private static float SampleHeight(ushort[] heightData, int width, int height, int x, int z)
    {
        int clampedX = Math.Clamp(x, 0, width - 1);
        int clampedZ = Math.Clamp(z, 0, height - 1);
        return heightData[clampedZ * width + clampedX] * HeightNormalization;
    }

    private static byte HashToByte(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)(x * 374761393);
            h = (h << 5) | (h >> 27);
            h ^= (uint)(z * 668265263);
            h *= 2246822519u;
            h ^= h >> 15;
            return (byte)h;
        }
    }
}
