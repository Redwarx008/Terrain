#nullable enable

using System;
using Terrain.Shared;

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
        RuntimeBiomeConfig? biomeConfig,
        float fallbackHeightScale,
        int biomeMaskResolutionRatio)
    {
        var indexData = new byte[(long)biomeMaskWidth * biomeMaskHeight * BytesPerPixel];
        var weightData = new byte[(long)biomeMaskWidth * biomeMaskHeight * BytesPerPixel];

        int defaultMaterialSlotIndex = GetDefaultMaterialSlotIndex(biomeConfig);

        if (biomeConfig == null || biomeConfig.BiomeLayers.Count == 0)
        {
            FillDefault(indexData, weightData, defaultMaterialSlotIndex);
            return new RuntimeDetailMapData(indexData, weightData, biomeMaskWidth, biomeMaskHeight);
        }

        var generationContext = new TerrainDetailGenerationContext(
            heightData,
            heightWidth,
            heightHeight,
            biomeConfig.HeightScale > 0.0f ? biomeConfig.HeightScale : fallbackHeightScale,
            biomeMaskData,
            biomeMaskWidth,
            biomeMaskHeight,
            biomeMaskResolutionRatio,
            defaultMaterialSlotIndex);

        for (int y = 0; y < biomeMaskHeight; y++)
        {
            for (int x = 0; x < biomeMaskWidth; x++)
            {
                TerrainDetailControlPixel pixel = TerrainDetailMapGenerator.EvaluatePixel(
                    generationContext,
                    biomeConfig.BiomeLayers,
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

    private static int GetDefaultMaterialSlotIndex(RuntimeBiomeConfig? biomeConfig)
    {
        if (biomeConfig == null)
            return 0;

        TerrainBiomeRuleLayer? firstDefaultBiomeLayer = null;
        foreach (TerrainBiomeRuleLayer layer in biomeConfig.BiomeLayers)
        {
            if (layer.BiomeId != 0)
                continue;

            firstDefaultBiomeLayer ??= layer;
            if (string.Equals(layer.Name, "Default Base", StringComparison.OrdinalIgnoreCase))
                return Math.Clamp(layer.MaterialSlotIndex, 0, byte.MaxValue);
        }

        return Math.Clamp(firstDefaultBiomeLayer?.MaterialSlotIndex ?? 0, 0, byte.MaxValue);
    }

    private static void FillDefault(byte[] indexData, byte[] weightData, int defaultMaterialSlotIndex)
    {
        byte defaultIndex = (byte)Math.Clamp(defaultMaterialSlotIndex, 0, byte.MaxValue);
        for (int i = 0; i < indexData.Length; i += BytesPerPixel)
        {
            indexData[i] = defaultIndex;
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
