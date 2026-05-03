#nullable enable

using System;
using System.Collections.Generic;

namespace Terrain.Shared;

public enum BiomeModifierType
{
    HeightRange = 0,
    SlopeRange = 1,
    CurvatureRange = 2,
    DirectionRange = 3,
    Noise = 4,
    TextureMask = 5,
}

public enum BiomeModifierBlendMode
{
    Multiply = 0,
    Add = 1,
    Subtract = 2,
    Min = 3,
    Max = 4,
}

public struct TerrainDetailControlPixel
{
    public byte Index0;
    public byte Index1;
    public byte Index2;
    public byte Index3;
    public byte Weight0;
    public byte Weight1;
    public byte Weight2;
    public byte Weight3;

    public static readonly TerrainDetailControlPixel Default = new()
    {
        Index0 = 0,
        Index1 = byte.MaxValue,
        Index2 = byte.MaxValue,
        Index3 = byte.MaxValue,
        Weight0 = byte.MaxValue,
    };
}

public sealed class TerrainBiomeModifier
{
    public string Name { get; set; } = "";
    public BiomeModifierType Type { get; set; }
    public BiomeModifierBlendMode BlendMode { get; set; } = BiomeModifierBlendMode.Multiply;
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public float Opacity { get; set; } = 1.0f;
    public float Min { get; set; }
    public float Max { get; set; } = 1.0f;
    public float MinFalloff { get; set; } = 0.001f;
    public float MaxFalloff { get; set; } = 0.001f;
    public float Radius { get; set; } = 1.0f;
    public float AngleDegrees { get; set; }
    public float AngleRangeDegrees { get; set; } = 180.0f;
    public float Scale { get; set; } = 1.0f;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Seed { get; set; }
    public float Octaves { get; set; } = 4.0f;
    public float Invert { get; set; }
    public string? TextureMaskPath { get; set; }
    public int TextureMaskChannel { get; set; }
}

public sealed class TerrainBiomeRuleLayer
{
    public string Name { get; set; } = "Layer";
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public int BiomeId { get; set; }
    public int MaterialSlotIndex { get; set; }
    public int PriorityOrder { get; set; }
    public List<TerrainBiomeModifier> Modifiers { get; } = new();
}

public sealed class TerrainDetailGenerationContext
{
    public TerrainDetailGenerationContext(
        ushort[] heightData,
        int heightWidth,
        int heightHeight,
        float heightScale,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        int biomeMaskToHeightRatio)
    {
        ArgumentNullException.ThrowIfNull(heightData);
        ArgumentNullException.ThrowIfNull(biomeMaskData);

        if (heightWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightWidth));
        if (heightHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightHeight));
        if (biomeMaskWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(biomeMaskWidth));
        if (biomeMaskHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(biomeMaskHeight));
        if (biomeMaskToHeightRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(biomeMaskToHeightRatio));
        if (heightData.Length != heightWidth * heightHeight)
            throw new ArgumentException("Height data length does not match dimensions.", nameof(heightData));
        if (biomeMaskData.Length != biomeMaskWidth * biomeMaskHeight)
            throw new ArgumentException("Biome mask length does not match dimensions.", nameof(biomeMaskData));

        HeightData = heightData;
        HeightWidth = heightWidth;
        HeightHeight = heightHeight;
        HeightScale = heightScale;
        BiomeMaskData = biomeMaskData;
        BiomeMaskWidth = biomeMaskWidth;
        BiomeMaskHeight = biomeMaskHeight;
        BiomeMaskToHeightRatio = biomeMaskToHeightRatio;
    }

    public ushort[] HeightData { get; }

    public int HeightWidth { get; }

    public int HeightHeight { get; }

    public float HeightScale { get; }

    public byte[] BiomeMaskData { get; }

    public int BiomeMaskWidth { get; }

    public int BiomeMaskHeight { get; }

    public int BiomeMaskToHeightRatio { get; }

    public byte GetBiomeId(int maskX, int maskY)
    {
        if ((uint)maskX >= (uint)BiomeMaskWidth || (uint)maskY >= (uint)BiomeMaskHeight)
            return 0;

        return BiomeMaskData[maskY * BiomeMaskWidth + maskX];
    }
}

public static class TerrainDetailMapGenerator
{
    private const float HeightSampleNormalization = 1.0f / ushort.MaxValue;

    public static TerrainDetailControlPixel EvaluatePixel(
        TerrainDetailGenerationContext context,
        IReadOnlyList<TerrainBiomeRuleLayer> layers,
        int maskX,
        int maskY)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(layers);

        byte biomeId = context.GetBiomeId(maskX, maskY);
        float altitude = SampleAverageAltitude(context, maskX, maskY);
        float slope = SampleSlopeDegrees(context, maskX, maskY);
        float directionDegrees = SampleDirectionDegrees(context, maskX, maskY);

        Span<int> bestIndices = stackalloc int[4] { 255, 255, 255, 255 };
        Span<float> bestWeights = stackalloc float[4];
        bool foundValidLayer = false;
        float remainingWeight = 1.0f;

        for (int layerIndex = layers.Count - 1; layerIndex >= 0; layerIndex--)
        {
            TerrainBiomeRuleLayer layer = layers[layerIndex];
            if (!layer.Enabled || !layer.Visible || layer.BiomeId != biomeId)
                continue;

            foundValidLayer = true;
            float weight = 1.0f;
            for (int modifierIndex = layer.Modifiers.Count - 1; modifierIndex >= 0; modifierIndex--)
            {
                TerrainBiomeModifier modifier = layer.Modifiers[modifierIndex];
                if (!modifier.Enabled)
                    continue;

                float modifierValue = EvaluateModifier(context, modifier, maskX, maskY, altitude, slope, directionDegrees);
                if (modifier.Invert > 0.5f)
                    modifierValue = 1.0f - modifierValue;

                float blended = ApplyBlendMode(weight, modifierValue, modifier.BlendMode);
                weight = Lerp(weight, blended, Saturate(modifier.Opacity));
            }

            weight = Saturate(weight);
            if (weight <= 0.0f)
                continue;

            float contribution = weight * remainingWeight;
            if (contribution <= 0.0f)
                continue;

            PushTop4(bestIndices, bestWeights, layer.MaterialSlotIndex, contribution);
            remainingWeight *= 1.0f - weight;
            if (remainingWeight <= 0.0001f)
                break;
        }

        if (!foundValidLayer)
            return TerrainDetailControlPixel.Default;

        if (remainingWeight > 0.0001f)
            PushTop4(bestIndices, bestWeights, 0, remainingWeight);

        float totalWeight = MathF.Max(bestWeights[0] + bestWeights[1] + bestWeights[2] + bestWeights[3], 0.0001f);
        Span<byte> encodedIndices = stackalloc byte[4];
        Span<byte> encodedWeights = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            encodedIndices[i] = (byte)Math.Clamp(bestIndices[i], 0, byte.MaxValue);
            encodedWeights[i] = (byte)Math.Clamp((int)MathF.Round(Saturate(bestWeights[i] / totalWeight) * byte.MaxValue), 0, byte.MaxValue);
        }

        return new TerrainDetailControlPixel
        {
            Index0 = encodedIndices[0],
            Index1 = encodedIndices[1],
            Index2 = encodedIndices[2],
            Index3 = encodedIndices[3],
            Weight0 = encodedWeights[0],
            Weight1 = encodedWeights[1],
            Weight2 = encodedWeights[2],
            Weight3 = encodedWeights[3],
        };
    }

    private static float EvaluateModifier(
        TerrainDetailGenerationContext context,
        TerrainBiomeModifier modifier,
        int maskX,
        int maskY,
        float altitude,
        float slope,
        float directionDegrees)
    {
        return modifier.Type switch
        {
            BiomeModifierType.HeightRange => ComputeRangeModifier(altitude, modifier.Min, modifier.Max, modifier.MinFalloff, modifier.MaxFalloff),
            BiomeModifierType.SlopeRange => ComputeRangeModifier(slope, modifier.Min, modifier.Max, modifier.MinFalloff, modifier.MaxFalloff),
            BiomeModifierType.CurvatureRange => EvaluateCurvatureModifier(context, modifier, maskX, maskY),
            BiomeModifierType.DirectionRange => EvaluateDirectionModifier(modifier, directionDegrees),
            BiomeModifierType.Noise => EvaluateNoiseModifier(context, modifier, maskX, maskY),
            BiomeModifierType.TextureMask => 1.0f,
            _ => 1.0f,
        };
    }

    private static float SampleAverageAltitude(TerrainDetailGenerationContext context, int maskX, int maskY)
    {
        ResolveMaskTexelToHeightCoord(context, maskX, maskY, out int heightX, out int heightY);
        return SampleHeightWorld(context, heightX, heightY);
    }

    private static float SampleSlopeDegrees(TerrainDetailGenerationContext context, int maskX, int maskY)
    {
        ResolveMaskTexelToHeightCoord(context, maskX, maskY, out int heightX, out int heightY);

        float left = SampleHeightWorld(context, heightX - 1, heightY);
        float right = SampleHeightWorld(context, heightX + 1, heightY);
        float up = SampleHeightWorld(context, heightX, heightY - 1);
        float down = SampleHeightWorld(context, heightX, heightY + 1);

        float worldNx = left - right;
        float worldNz = up - down;
        const float worldNy = 2.0f;

        float normalLength = MathF.Sqrt(worldNx * worldNx + worldNz * worldNz + worldNy * worldNy);
        if (normalLength <= 0.0001f)
            return 0.0f;

        float cosSlope = Math.Clamp(worldNy / normalLength, -1.0f, 1.0f);
        return MathF.Acos(cosSlope) * (180.0f / MathF.PI);
    }

    private static float SampleCurvature(TerrainDetailGenerationContext context, int maskX, int maskY, float radius)
    {
        ResolveMaskTexelToHeightCoord(context, maskX, maskY, out int heightX, out int heightY);
        int sampleRadius = Math.Clamp((int)(radius + 0.5f), 1, 16);

        float centerHeight = SampleHeightWorld(context, heightX, heightY);
        float left = SampleHeightWorld(context, heightX - sampleRadius, heightY);
        float right = SampleHeightWorld(context, heightX + sampleRadius, heightY);
        float up = SampleHeightWorld(context, heightX, heightY - sampleRadius);
        float down = SampleHeightWorld(context, heightX, heightY + sampleRadius);

        float leftSlope = centerHeight - left;
        float rightSlope = right - centerHeight;
        float upSlope = centerHeight - up;
        float downSlope = down - centerHeight;

        float denominator = MathF.Max(
            MathF.Abs(leftSlope) + MathF.Abs(rightSlope) + MathF.Abs(upSlope) + MathF.Abs(downSlope),
            0.0001f);
        float signedCurvature = (leftSlope - rightSlope + upSlope - downSlope) / denominator;
        return Saturate(signedCurvature * 0.5f + 0.5f);
    }

    private static float SampleDirectionDegrees(TerrainDetailGenerationContext context, int maskX, int maskY)
    {
        ResolveMaskTexelToHeightCoord(context, maskX, maskY, out int heightX, out int heightY);
        float left = SampleHeightWorld(context, heightX - 1, heightY);
        float right = SampleHeightWorld(context, heightX + 1, heightY);
        float up = SampleHeightWorld(context, heightX, heightY - 1);
        float down = SampleHeightWorld(context, heightX, heightY + 1);
        return MathF.Atan2(down - up, right - left) * (180.0f / MathF.PI);
    }

    private static float SampleHeightNormalized(TerrainDetailGenerationContext context, int x, int y)
    {
        int clampedX = Math.Clamp(x, 0, context.HeightWidth - 1);
        int clampedY = Math.Clamp(y, 0, context.HeightHeight - 1);
        return context.HeightData[clampedY * context.HeightWidth + clampedX] * HeightSampleNormalization;
    }

    private static float SampleHeightWorld(TerrainDetailGenerationContext context, int x, int y)
    {
        return SampleHeightNormalized(context, x, y) * context.HeightScale;
    }

    private static void ResolveMaskTexelToHeightCoord(TerrainDetailGenerationContext context, int maskX, int maskY, out int heightX, out int heightY)
    {
        heightX = Math.Clamp(maskX * context.BiomeMaskToHeightRatio, 0, context.HeightWidth - 1);
        heightY = Math.Clamp(maskY * context.BiomeMaskToHeightRatio, 0, context.HeightHeight - 1);
    }

    private static float EvaluateCurvatureModifier(TerrainDetailGenerationContext context, TerrainBiomeModifier modifier, int maskX, int maskY)
    {
        float curvature = SampleCurvature(context, maskX, maskY, modifier.Radius);
        float min = modifier.Min;
        float max = modifier.Max;
        float minFalloff = modifier.MinFalloff;
        float maxFalloff = modifier.MaxFalloff;

        if (min < 0.0f || max > 1.0f)
        {
            min = Saturate(min * 0.5f + 0.5f);
            max = Saturate(max * 0.5f + 0.5f);
            minFalloff *= 0.5f;
            maxFalloff *= 0.5f;
        }

        return ComputeRangeModifier(curvature, min, max, minFalloff, maxFalloff);
    }

    private static float EvaluateDirectionModifier(TerrainBiomeModifier modifier, float directionDegrees)
    {
        float delta = MathF.Abs(directionDegrees - modifier.AngleDegrees);
        delta = MathF.Min(delta, 360.0f - delta);
        return 1.0f - Saturate(delta / MathF.Max(modifier.AngleRangeDegrees, 0.0001f));
    }

    private static float EvaluateNoiseModifier(TerrainDetailGenerationContext context, TerrainBiomeModifier modifier, int maskX, int maskY)
    {
        ResolveMaskTexelToHeightCoord(context, maskX, maskY, out int heightX, out int heightY);
        float scale = MathF.Max(modifier.Scale, 0.0001f);
        float px = heightX * scale + modifier.OffsetX;
        float py = heightY * scale + modifier.OffsetY;
        return Saturate(Fbm(px, py, modifier.Seed, modifier.Octaves));
    }

    private static float ComputeRangeModifier(float value, float minValue, float maxValue, float minFalloff, float maxFalloff)
    {
        float minEnd = minValue - minFalloff;
        float minDenominator = minEnd - minValue;
        float minWeight = Saturate((minEnd - (value - minValue)) / (MathF.Abs(minDenominator) > 0.0001f ? minDenominator : -0.0001f));
        float maxEnd = maxValue + maxFalloff;
        float maxDenominator = maxEnd - maxValue;
        float maxWeight = Saturate((maxEnd - (value - maxValue)) / (MathF.Abs(maxDenominator) > 0.0001f ? maxDenominator : 0.0001f));
        return Saturate(minWeight * maxWeight);
    }

    private static float ApplyBlendMode(float baseWeight, float modifierValue, BiomeModifierBlendMode blendMode)
    {
        float result = blendMode switch
        {
            BiomeModifierBlendMode.Multiply => baseWeight * modifierValue,
            BiomeModifierBlendMode.Add => baseWeight + modifierValue,
            BiomeModifierBlendMode.Subtract => baseWeight - modifierValue,
            BiomeModifierBlendMode.Min => MathF.Min(baseWeight, modifierValue),
            BiomeModifierBlendMode.Max => MathF.Max(baseWeight, modifierValue),
            _ => baseWeight,
        };

        return Saturate(result);
    }

    private static void PushTop4(Span<int> bestIndices, Span<float> bestWeights, int materialIndex, float weight)
    {
        if (weight <= 0.0f)
            return;

        for (int i = 0; i < 4; i++)
        {
            if (bestIndices[i] != materialIndex)
                continue;

            bestWeights[i] += weight;
            return;
        }

        if (weight > bestWeights[0])
        {
            bestWeights[3] = bestWeights[2]; bestIndices[3] = bestIndices[2];
            bestWeights[2] = bestWeights[1]; bestIndices[2] = bestIndices[1];
            bestWeights[1] = bestWeights[0]; bestIndices[1] = bestIndices[0];
            bestWeights[0] = weight; bestIndices[0] = materialIndex;
        }
        else if (weight > bestWeights[1])
        {
            bestWeights[3] = bestWeights[2]; bestIndices[3] = bestIndices[2];
            bestWeights[2] = bestWeights[1]; bestIndices[2] = bestIndices[1];
            bestWeights[1] = weight; bestIndices[1] = materialIndex;
        }
        else if (weight > bestWeights[2])
        {
            bestWeights[3] = bestWeights[2]; bestIndices[3] = bestIndices[2];
            bestWeights[2] = weight; bestIndices[2] = materialIndex;
        }
        else if (weight > bestWeights[3])
        {
            bestWeights[3] = weight; bestIndices[3] = materialIndex;
        }
    }

    private static float Fbm(float x, float y, float seed, float octaves)
    {
        int octaveCount = Math.Clamp((int)(octaves + 0.5f), 1, 8);
        float amplitude = 0.5f;
        float frequency = 1.0f;
        float sum = 0.0f;
        float normalization = 0.0f;

        for (int octave = 0; octave < octaveCount; octave++)
        {
            sum += Noise2D(x * frequency, y * frequency, seed + octave * 17.0f) * amplitude;
            normalization += amplitude;
            frequency *= 2.0f;
            amplitude *= 0.5f;
        }

        return normalization > 0.0001f ? sum / normalization : 0.0f;
    }

    private static float Noise2D(float x, float y, float seed)
    {
        float ix = MathF.Floor(x);
        float iy = MathF.Floor(y);
        float fx = x - ix;
        float fy = y - iy;
        float a = Hash11(ix * 127.1f + iy * 311.7f + seed);
        float b = Hash11((ix + 1.0f) * 127.1f + iy * 311.7f + seed);
        float c = Hash11(ix * 127.1f + (iy + 1.0f) * 311.7f + seed);
        float d = Hash11((ix + 1.0f) * 127.1f + (iy + 1.0f) * 311.7f + seed);
        float ux = fx * fx * (3.0f - 2.0f * fx);
        float uy = fy * fy * (3.0f - 2.0f * fy);
        return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
    }

    private static float Hash11(float n)
    {
        float value = MathF.Sin(n) * 43758.5453123f;
        return value - MathF.Floor(value);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private static float Saturate(float value)
    {
        return Math.Clamp(value, 0.0f, 1.0f);
    }
}
