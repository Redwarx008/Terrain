#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrain.Resources;

namespace Terrain.Editor.Services.Resources;

public sealed record EditorResolvedMaterialSlot(
    int SlotIndex,
    string MaterialId,
    string Name,
    string? AlbedoTexturePath,
    string? NormalTexturePath,
    string? PropertiesTexturePath,
    bool IsRuntimeFallbackPlaceholder,
    bool UsesFallbackAlbedo,
    bool UsesFallbackNormal);

public sealed class EditorMaterialRecoveryResult
{
    public required IReadOnlyList<EditorResolvedMaterialSlot> Slots { get; init; }
    public required IReadOnlyDictionary<string, int> MaterialIndicesById { get; init; }
    public required EditorMaterialLoadState LoadState { get; init; }
}

public sealed class EditorMaterialRecoveryService
{
    public EditorMaterialRecoveryResult Recover(
        RuntimeMaterialDescriptor descriptor,
        string descriptorPath,
        RuntimeBiomeSettings biomeSettings)
    {
        string descriptorDirectory = Path.GetDirectoryName(descriptorPath)
            ?? throw new InvalidDataException($"Material descriptor path has no directory: {descriptorPath}");

        var slots = new List<EditorResolvedMaterialSlot>();
        var materialIndicesById = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedIndices = new HashSet<int>();
        var issues = new List<EditorMaterialLoadIssue>();
        bool hasTextureFallbacks = false;
        bool hasBlockingMissingMaterialIds = false;

        foreach (RuntimeMaterialEntry material in descriptor.Materials)
        {
            usedIndices.Add(material.Index);

            string? albedoPath = ResolveDescriptorTexturePath(descriptorDirectory, material.AlbedoPath);
            string? normalPath = ResolveDescriptorTexturePath(descriptorDirectory, material.NormalPath);
            string? propertiesPath = ResolveDescriptorTexturePath(descriptorDirectory, material.PropertiesPath);

            bool usesFallbackAlbedo = !string.IsNullOrWhiteSpace(albedoPath) && !File.Exists(albedoPath);
            bool usesFallbackNormal = !string.IsNullOrWhiteSpace(normalPath) && !File.Exists(normalPath);

            if (usesFallbackAlbedo)
            {
                hasTextureFallbacks = true;
                issues.Add(new EditorMaterialLoadIssue(
                    EditorMaterialLoadIssueKind.MissingAlbedoTexture,
                    material.Id,
                    $"Terrain material '{material.Id}' is missing albedo texture. Falling back to magenta missing-material diffuse: {albedoPath}",
                    albedoPath));
            }

            if (usesFallbackNormal)
            {
                hasTextureFallbacks = true;
                issues.Add(new EditorMaterialLoadIssue(
                    EditorMaterialLoadIssueKind.MissingNormalTexture,
                    material.Id,
                    $"Terrain material '{material.Id}' is missing normal texture. Falling back to flat normal: {normalPath}",
                    normalPath));
            }

            if (!string.IsNullOrWhiteSpace(propertiesPath) && !File.Exists(propertiesPath))
            {
                hasTextureFallbacks = true;
                issues.Add(new EditorMaterialLoadIssue(
                    EditorMaterialLoadIssueKind.MissingPropertiesTexture,
                    material.Id,
                    $"Terrain material '{material.Id}' is missing properties texture: {propertiesPath}",
                    propertiesPath));
            }

            slots.Add(new EditorResolvedMaterialSlot(
                material.Index,
                material.Id,
                material.Name,
                albedoPath,
                normalPath,
                propertiesPath,
                IsRuntimeFallbackPlaceholder: false,
                UsesFallbackAlbedo: usesFallbackAlbedo,
                UsesFallbackNormal: usesFallbackNormal));
            materialIndicesById[material.Id] = material.Index;
        }

        foreach (RuntimeBiomeLayerEntry layer in biomeSettings.Layers)
        {
            if (materialIndicesById.ContainsKey(layer.MaterialId))
                continue;

            int slotIndex = ReserveFallbackSlotIndex(usedIndices);
            hasBlockingMissingMaterialIds = true;
            issues.Add(new EditorMaterialLoadIssue(
                EditorMaterialLoadIssueKind.MissingMaterialId,
                layer.MaterialId,
                $"Terrain material id '{layer.MaterialId}' is referenced by biome settings but missing from descriptor: {descriptorPath}",
                descriptorPath));

            slots.Add(new EditorResolvedMaterialSlot(
                slotIndex,
                layer.MaterialId,
                $"Missing:{layer.MaterialId}",
                AlbedoTexturePath: null,
                NormalTexturePath: null,
                PropertiesTexturePath: null,
                IsRuntimeFallbackPlaceholder: true,
                UsesFallbackAlbedo: true,
                UsesFallbackNormal: true));
            materialIndicesById[layer.MaterialId] = slotIndex;
        }

        return new EditorMaterialRecoveryResult
        {
            Slots = slots.OrderBy(static slot => slot.SlotIndex).ToArray(),
            MaterialIndicesById = materialIndicesById,
            LoadState = new EditorMaterialLoadState(
                issues,
                hasBlockingMissingMaterialIds,
                hasTextureFallbacks),
        };
    }

    private static int ReserveFallbackSlotIndex(HashSet<int> usedIndices)
    {
        for (int index = 0; index < 256; index++)
        {
            if (usedIndices.Add(index))
                return index;
        }

        throw new InvalidDataException("No free material slot remains for runtime fallback.");
    }

    private static string? ResolveDescriptorTexturePath(string descriptorDirectory, string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        return Path.Combine(descriptorDirectory, texturePath.Trim());
    }
}
