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
