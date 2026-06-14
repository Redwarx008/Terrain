#nullable enable

using System;
using System.Collections.Generic;
using Terrain.Resources;

namespace Terrain;

internal readonly record struct RuntimeDetailMapData(
    byte[] IndexData,
    byte[] WeightData,
    int Width,
    int Height);

internal static class RuntimeDetailMapBuilder
{
    private const int BytesPerPixel = 4;

    public static RuntimeDetailMapData Generate(
        ushort[] heightData,
        int heightWidth,
        int heightHeight,
        byte[] biomeMaskData,
        int biomeMaskWidth,
        int biomeMaskHeight,
        RuntimeBiomeSettings biomeSettings,
        RuntimeMaterialDescriptor materialDescriptor,
        float heightScale,
        int biomeMaskResolutionRatio)
    {
        var indexData = new byte[(long)biomeMaskWidth * biomeMaskHeight * BytesPerPixel];
        var weightData = new byte[(long)biomeMaskWidth * biomeMaskHeight * BytesPerPixel];
        var biomeLayers = BuildRuleLayers(biomeSettings, materialDescriptor);

        if (biomeLayers.Count == 0)
        {
            FillDefault(indexData, weightData);
            return new RuntimeDetailMapData(indexData, weightData, biomeMaskWidth, biomeMaskHeight);
        }

        var generationContext = new TerrainDetailGenerationContext(
            heightData,
            heightWidth,
            heightHeight,
            heightScale,
            biomeMaskData,
            biomeMaskWidth,
            biomeMaskHeight,
            biomeMaskResolutionRatio);

        for (int y = 0; y < biomeMaskHeight; y++)
        {
            for (int x = 0; x < biomeMaskWidth; x++)
            {
                TerrainDetailControlPixel pixel = TerrainDetailMapGenerator.EvaluatePixel(
                    generationContext,
                    biomeLayers,
                    x,
                    y);
                int offset = (y * biomeMaskWidth + x) * BytesPerPixel;
                indexData[offset] = pixel.Index0;
                indexData[offset + 1] = pixel.Index1;
                indexData[offset + 2] = pixel.Index2;
                indexData[offset + 3] = pixel.Index3;
                weightData[offset] = pixel.Weight0;
                weightData[offset + 1] = pixel.Weight1;
                weightData[offset + 2] = pixel.Weight2;
                weightData[offset + 3] = pixel.Weight3;
            }
        }

        return new RuntimeDetailMapData(indexData, weightData, biomeMaskWidth, biomeMaskHeight);
    }

    private static List<TerrainBiomeRuleLayer> BuildRuleLayers(
        RuntimeBiomeSettings biomeSettings,
        RuntimeMaterialDescriptor materialDescriptor)
    {
        var materialIndicesById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RuntimeMaterialEntry material in materialDescriptor.Materials)
        {
            materialIndicesById[material.Id] = material.Index;
        }

        var layersById = new Dictionary<int, TerrainBiomeRuleLayer>();
        var layers = new List<TerrainBiomeRuleLayer>(biomeSettings.Layers.Count);
        foreach (RuntimeBiomeLayerEntry sourceLayer in biomeSettings.Layers)
        {
            materialIndicesById.TryGetValue(sourceLayer.MaterialId, out int materialIndex);
            var layer = new TerrainBiomeRuleLayer
            {
                Name = sourceLayer.Name,
                BiomeId = sourceLayer.BiomeId,
                Enabled = sourceLayer.Enabled,
                Visible = sourceLayer.Visible,
                MaterialSlotIndex = materialIndex,
                PriorityOrder = sourceLayer.Priority,
            };

            layersById[sourceLayer.Id] = layer;
            layers.Add(layer);
        }

        foreach (RuntimeBiomeModifierEntry sourceModifier in biomeSettings.Modifiers)
        {
            if (!layersById.TryGetValue(sourceModifier.LayerId, out TerrainBiomeRuleLayer? layer))
                continue;

            layer.Modifiers.Add(new TerrainBiomeModifier
            {
                Name = sourceModifier.Name,
                Type = ParseEnum(sourceModifier.Type, BiomeModifierType.HeightRange),
                BlendMode = ParseEnum(sourceModifier.BlendMode, BiomeModifierBlendMode.Multiply),
                Enabled = sourceModifier.Enabled,
                Visible = sourceModifier.Visible,
                Opacity = sourceModifier.Opacity,
                Min = sourceModifier.Min,
                Max = sourceModifier.Max,
                MinFalloff = sourceModifier.MinFalloff,
                MaxFalloff = sourceModifier.MaxFalloff,
                Radius = sourceModifier.Radius,
                AngleDegrees = sourceModifier.AngleDegrees,
                AngleRangeDegrees = sourceModifier.AngleRangeDegrees,
                Scale = sourceModifier.Scale,
                OffsetX = sourceModifier.OffsetX,
                OffsetY = sourceModifier.OffsetY,
                Seed = sourceModifier.Seed,
                Octaves = sourceModifier.Octaves,
                Invert = sourceModifier.Invert,
                TextureMaskPath = sourceModifier.TextureMaskPath,
                TextureMaskChannel = sourceModifier.TextureMaskChannel,
            });
        }

        layers.Sort(static (left, right) => left.PriorityOrder.CompareTo(right.PriorityOrder));
        return layers;
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
            ? parsed
            : fallback;
    }

    private static void FillDefault(byte[] indexData, byte[] weightData)
    {
        for (int i = 0; i < indexData.Length; i += BytesPerPixel)
        {
            indexData[i] = 0;
            indexData[i + 1] = byte.MaxValue;
            indexData[i + 2] = byte.MaxValue;
            indexData[i + 3] = byte.MaxValue;
            weightData[i] = byte.MaxValue;
            weightData[i + 1] = 0;
            weightData[i + 2] = 0;
            weightData[i + 3] = 0;
        }
    }
}
