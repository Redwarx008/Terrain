#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Terrain.Editor.Services.Export;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct DetailControlPixel(byte R, byte G, byte B, byte A)
{
    public static readonly DetailControlPixel DefaultIndex = new(0, byte.MaxValue, byte.MaxValue, byte.MaxValue);
    public static readonly DetailControlPixel DefaultWeight = new(byte.MaxValue, 0, 0, 0);
}

internal readonly record struct BakedDetailMapData(
    DetailControlPixel[] DetailIndex,
    DetailControlPixel[] DetailWeight,
    int Width,
    int Height);

internal static class BakedDetailMapBuilder
{
    public static BakedDetailMapData Generate(
        ushort[] heightData,
        int heightWidth,
        int heightHeight,
        float heightScale,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        IReadOnlyList<BiomeRuleLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(heightData);

        if (heightWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightWidth));
        if (heightHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightHeight));
        if (heightData.Length != heightWidth * heightHeight)
            throw new ArgumentException("Height data length does not match dimensions.", nameof(heightData));

        float heightScaleFactor = heightScale / ushort.MaxValue;
        return Generate(
            (x, y) => heightData[y * heightWidth + x] * heightScaleFactor,
            heightWidth,
            heightHeight,
            biomeMaskData,
            biomeMaskWidth,
            biomeMaskHeight,
            layers);
    }

    public static BakedDetailMapData Generate(
        Func<int, int, float> getHeight,
        int heightWidth,
        int heightHeight,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        IReadOnlyList<BiomeRuleLayer> layers)
    {
        return Generate(
            getHeight,
            heightWidth,
            heightHeight,
            biomeMaskData,
            biomeMaskWidth,
            biomeMaskHeight,
            layers,
            (heightWidth + 1) / 2,
            (heightHeight + 1) / 2);
    }

    public static BakedDetailMapData Generate(
        Func<int, int, float> getHeight,
        int heightWidth,
        int heightHeight,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        IReadOnlyList<BiomeRuleLayer> layers,
        int detailWidth,
        int detailHeight)
    {
        ArgumentNullException.ThrowIfNull(getHeight);
        ArgumentNullException.ThrowIfNull(biomeMaskData);
        ArgumentNullException.ThrowIfNull(layers);

        if (heightWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightWidth));
        if (heightHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightHeight));
        if (biomeMaskWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(biomeMaskWidth));
        if (biomeMaskHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(biomeMaskHeight));
        if (biomeMaskData.Length != biomeMaskWidth * biomeMaskHeight)
            throw new ArgumentException("Biome mask length does not match dimensions.", nameof(biomeMaskData));
        if (detailWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(detailWidth));
        if (detailHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(detailHeight));

        var detailIndex = new DetailControlPixel[checked(detailWidth * detailHeight)];
        var detailWeight = new DetailControlPixel[detailIndex.Length];
        var orderedLayers = layers.OrderBy(static layer => layer.PriorityOrder).ToArray();
        var context = new DetailEvaluationContext(
            getHeight,
            heightWidth,
            heightHeight,
            biomeMaskData,
            biomeMaskWidth,
            biomeMaskHeight,
            detailWidth,
            detailHeight);

        for (int y = 0; y < detailHeight; y++)
        {
            for (int x = 0; x < detailWidth; x++)
            {
                DetailControlPair pixel = EvaluatePixel(context, orderedLayers, x, y);
                int offset = y * detailWidth + x;
                detailIndex[offset] = pixel.Index;
                detailWeight[offset] = pixel.Weight;
            }
        }

        return new BakedDetailMapData(detailIndex, detailWeight, detailWidth, detailHeight);
    }

    private static DetailControlPair EvaluatePixel(
        DetailEvaluationContext context,
        IReadOnlyList<BiomeRuleLayer> layers,
        int detailX,
        int detailY)
    {
        byte biomeId = context.GetBiomeId(detailX, detailY);
        ResolveDetailTexelToHeightCoord(context, detailX, detailY, out int heightX, out int heightY);
        float altitude = SampleHeightWorld(context, heightX, heightY);
        float slope = SampleSlopeDegrees(context, heightX, heightY);
        float directionDegrees = SampleDirectionDegrees(context, heightX, heightY);

        Span<int> bestIndices = stackalloc int[4] { byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue };
        Span<float> bestWeights = stackalloc float[4];
        bool foundValidLayer = false;
        float remainingWeight = 1.0f;
        int fallbackMaterialSlotIndex = 0;

        for (int layerIndex = layers.Count - 1; layerIndex >= 0; layerIndex--)
        {
            BiomeRuleLayer layer = layers[layerIndex];
            if (!layer.Enabled || !layer.Visible || layer.BiomeId != biomeId)
                continue;

            foundValidLayer = true;
            fallbackMaterialSlotIndex = layer.MaterialSlotIndex;
            float weight = 1.0f;
            for (int modifierIndex = layer.Modifiers.Count - 1; modifierIndex >= 0; modifierIndex--)
            {
                BiomeModifier modifier = layer.Modifiers[modifierIndex];
                if (!modifier.Enabled)
                    continue;

                float modifierValue = EvaluateModifier(context, modifier, detailX, detailY, heightX, heightY, altitude, slope, directionDegrees);
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
            return DetailControlPair.Default;

        if (remainingWeight > 0.0001f)
            PushTop4(bestIndices, bestWeights, fallbackMaterialSlotIndex, remainingWeight);

        float totalWeight = MathF.Max(bestWeights[0] + bestWeights[1] + bestWeights[2] + bestWeights[3], 0.0001f);
        return new DetailControlPair(
            new DetailControlPixel(
                (byte)Math.Clamp(bestIndices[0], 0, byte.MaxValue),
                (byte)Math.Clamp(bestIndices[1], 0, byte.MaxValue),
                (byte)Math.Clamp(bestIndices[2], 0, byte.MaxValue),
                (byte)Math.Clamp(bestIndices[3], 0, byte.MaxValue)),
            new DetailControlPixel(
                EncodeWeight(bestWeights[0], totalWeight),
                EncodeWeight(bestWeights[1], totalWeight),
                EncodeWeight(bestWeights[2], totalWeight),
                EncodeWeight(bestWeights[3], totalWeight)));
    }

    private static float EvaluateModifier(
        DetailEvaluationContext context,
        BiomeModifier modifier,
        int detailX,
        int detailY,
        int heightX,
        int heightY,
        float altitude,
        float slope,
        float directionDegrees)
    {
        return modifier.Type switch
        {
            BiomeModifierType.HeightRange => ComputeRangeModifier(altitude, modifier.Min, modifier.Max, modifier.MinFalloff, modifier.MaxFalloff),
            BiomeModifierType.SlopeRange => ComputeRangeModifier(slope, modifier.Min, modifier.Max, modifier.MinFalloff, modifier.MaxFalloff),
            BiomeModifierType.CurvatureRange => EvaluateCurvatureModifier(context, modifier, heightX, heightY),
            BiomeModifierType.DirectionRange => EvaluateDirectionModifier(modifier, directionDegrees),
            BiomeModifierType.Noise => EvaluateNoiseModifier(context, modifier, heightX, heightY),
            BiomeModifierType.TextureMask => 1.0f,
            _ => 1.0f,
        };
    }

    private static void ResolveDetailTexelToHeightCoord(DetailEvaluationContext context, int detailX, int detailY, out int heightX, out int heightY)
    {
        heightX = Math.Clamp(detailX * 2, 0, context.HeightWidth - 1);
        heightY = Math.Clamp(detailY * 2, 0, context.HeightHeight - 1);
    }

    private static float SampleHeightWorld(DetailEvaluationContext context, int x, int y)
    {
        int clampedX = Math.Clamp(x, 0, context.HeightWidth - 1);
        int clampedY = Math.Clamp(y, 0, context.HeightHeight - 1);
        return context.GetHeight(clampedX, clampedY);
    }

    private static float SampleSlopeDegrees(DetailEvaluationContext context, int x, int y)
    {
        float left = SampleHeightWorld(context, x - 1, y);
        float right = SampleHeightWorld(context, x + 1, y);
        float up = SampleHeightWorld(context, x, y - 1);
        float down = SampleHeightWorld(context, x, y + 1);

        float worldNx = left - right;
        float worldNz = up - down;
        const float worldNy = 2.0f;

        float normalLength = MathF.Sqrt(worldNx * worldNx + worldNz * worldNz + worldNy * worldNy);
        if (normalLength <= 0.0001f)
            return 0.0f;

        float cosSlope = Math.Clamp(worldNy / normalLength, -1.0f, 1.0f);
        return MathF.Acos(cosSlope) * (180.0f / MathF.PI);
    }

    private static float SampleDirectionDegrees(DetailEvaluationContext context, int x, int y)
    {
        float left = SampleHeightWorld(context, x - 1, y);
        float right = SampleHeightWorld(context, x + 1, y);
        float up = SampleHeightWorld(context, x, y - 1);
        float down = SampleHeightWorld(context, x, y + 1);
        return MathF.Atan2(down - up, right - left) * (180.0f / MathF.PI);
    }

    private static float EvaluateCurvatureModifier(DetailEvaluationContext context, BiomeModifier modifier, int heightX, int heightY)
    {
        int sampleRadius = Math.Clamp((int)(modifier.Radius + 0.5f), 1, 16);

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

        float curvature = Saturate((leftSlope - rightSlope + upSlope - downSlope) / denominator * 0.5f + 0.5f);
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

    private static float EvaluateDirectionModifier(BiomeModifier modifier, float directionDegrees)
    {
        float delta = MathF.Abs(directionDegrees - modifier.AngleDegrees);
        delta = MathF.Min(delta, 360.0f - delta);
        return 1.0f - Saturate(delta / MathF.Max(modifier.AngleRangeDegrees, 0.0001f));
    }

    private static float EvaluateNoiseModifier(DetailEvaluationContext context, BiomeModifier modifier, int heightX, int heightY)
    {
        float scale = MathF.Max(modifier.Scale, 0.0001f);
        float px = heightX * scale + modifier.OffsetX;
        float py = heightY * scale + modifier.OffsetY;
        return Saturate(Fbm(px, py, modifier.Seed, modifier.Octaves));
    }

    private static float ComputeRangeModifier(float value, float minValue, float maxValue, float minFalloff, float maxFalloff)
    {
        float minStart = minValue - minFalloff;
        float minWeight = Saturate((value - minStart) / MathF.Max(minValue - minStart, 0.001f));
        float maxEnd = maxValue + maxFalloff;
        float maxWeight = Saturate((maxEnd - value) / MathF.Max(maxEnd - maxValue, 0.001f));
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

    private static byte EncodeWeight(float value, float totalWeight)
    {
        return (byte)Math.Clamp((int)MathF.Round(Saturate(value / totalWeight) * byte.MaxValue), 0, byte.MaxValue);
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

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Saturate(float value) => Math.Clamp(value, 0.0f, 1.0f);

    private readonly record struct DetailControlPair(DetailControlPixel Index, DetailControlPixel Weight)
    {
        public static readonly DetailControlPair Default = new(DetailControlPixel.DefaultIndex, DetailControlPixel.DefaultWeight);
    }

    private sealed class DetailEvaluationContext(
        Func<int, int, float> getHeight,
        int heightWidth,
        int heightHeight,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        int detailWidth,
        int detailHeight)
    {
        public Func<int, int, float> GetHeight { get; } = getHeight;
        public int HeightWidth { get; } = heightWidth;
        public int HeightHeight { get; } = heightHeight;
        public byte[] BiomeMaskData { get; } = biomeMaskData;
        public int BiomeMaskWidth { get; } = biomeMaskWidth;
        public int BiomeMaskHeight { get; } = biomeMaskHeight;
        public int DetailWidth { get; } = detailWidth;
        public int DetailHeight { get; } = detailHeight;

        public byte GetBiomeId(int detailX, int detailY)
        {
            int maskX = Math.Clamp((int)((long)detailX * BiomeMaskWidth / DetailWidth), 0, BiomeMaskWidth - 1);
            int maskY = Math.Clamp((int)((long)detailY * BiomeMaskHeight / DetailHeight), 0, BiomeMaskHeight - 1);
            return BiomeMaskData[maskY * BiomeMaskWidth + maskX];
        }
    }
}
