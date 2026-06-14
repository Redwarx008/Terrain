#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Terrain.Editor.Services.Resources;

public sealed class EditorBiomeSettingsSnapshot
{
    public EditorBiomeSettingsSnapshot(
        IReadOnlyList<EditorBiomeDefinition> biomes,
        IReadOnlyList<EditorBiomeLayerDefinition> layers,
        IReadOnlyList<EditorBiomeModifierDefinition> modifiers)
    {
        Biomes = biomes;
        Layers = layers;
        Modifiers = modifiers;
    }

    public IReadOnlyList<EditorBiomeDefinition> Biomes { get; }
    public IReadOnlyList<EditorBiomeLayerDefinition> Layers { get; }
    public IReadOnlyList<EditorBiomeModifierDefinition> Modifiers { get; }
}

public static class EditorAuthoringResourceMapper
{
    public static IReadOnlyList<EditorMaterialDescriptorSlot> CreateMaterialDescriptorSlots(IEnumerable<MaterialSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var result = new List<EditorMaterialDescriptorSlot>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (MaterialSlot slot in slots)
        {
            if (slot.IsEmpty)
                continue;

            string id = CreateUniqueMaterialId(slot, usedIds);
            result.Add(new EditorMaterialDescriptorSlot(
                id,
                slot.Index,
                string.IsNullOrWhiteSpace(slot.Name) ? id : slot.Name.Trim(),
                ToFileName(slot.AlbedoTexturePath),
                ToFileName(slot.NormalTexturePath),
                ToFileName(slot.PropertiesTexturePath)));
        }

        return result;
    }

    public static EditorBiomeSettingsSnapshot CreateBiomeSettingsSnapshot(
        BiomeRuleService service,
        IReadOnlyDictionary<int, string> materialIdsByIndex)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(materialIdsByIndex);

        var biomes = service.Biomes
            .Select(static biome => new EditorBiomeDefinition(biome.Id, biome.Name))
            .ToArray();

        var layers = new List<EditorBiomeLayerDefinition>();
        var modifiers = new List<EditorBiomeModifierDefinition>();
        foreach (BiomeRuleLayer layer in service.Layers)
        {
            if (!materialIdsByIndex.TryGetValue(layer.MaterialSlotIndex, out string? materialId)
                || string.IsNullOrWhiteSpace(materialId))
            {
                throw new InvalidOperationException($"Missing material id for material slot index {layer.MaterialSlotIndex}.");
            }

            layers.Add(new EditorBiomeLayerDefinition(
                layer.Id,
                layer.BiomeId,
                layer.Name,
                materialId,
                layer.PriorityOrder,
                layer.Enabled,
                layer.Visible));

            layer.EnsureLegacyModifiers();
            foreach (BiomeModifier modifier in layer.Modifiers)
            {
                modifiers.Add(new EditorBiomeModifierDefinition(
                    modifier.Id,
                    layer.Id,
                    string.IsNullOrWhiteSpace(modifier.Name) ? modifier.Type.ToString() : modifier.Name,
                    modifier.Type.ToString(),
                    modifier.BlendMode.ToString(),
                    modifier.Min,
                    modifier.Max,
                    modifier.MinFalloff,
                    modifier.MaxFalloff,
                    modifier.Radius,
                    modifier.AngleDegrees,
                    modifier.AngleRangeDegrees,
                    modifier.Scale,
                    modifier.OffsetX,
                    modifier.OffsetY,
                    modifier.Seed,
                    modifier.Octaves,
                    modifier.Invert,
                    modifier.TextureMaskPath,
                    modifier.TextureMaskChannel,
                    modifier.Opacity,
                    modifier.Enabled,
                    modifier.Visible));
            }
        }

        return new EditorBiomeSettingsSnapshot(biomes, layers, modifiers);
    }

    private static string CreateUniqueMaterialId(MaterialSlot slot, HashSet<string> usedIds)
    {
        if (!string.IsNullOrWhiteSpace(slot.MaterialId))
        {
            string stableId = ReserveUniqueMaterialId(slot.MaterialId.Trim(), slot.Index, usedIds);
            slot.MaterialId = stableId;
            return stableId;
        }

        string source = HasMeaningfulName(slot)
            ? slot.Name
            : Path.GetFileNameWithoutExtension(slot.AlbedoTexturePath) ?? string.Empty;

        string baseId = ToDescriptorSafeId(source);
        if (baseId.Length == 0)
            baseId = $"material_{slot.Index}";

        string generatedId = ReserveUniqueMaterialId(baseId, slot.Index, usedIds);
        slot.MaterialId = generatedId;
        return generatedId;
    }

    private static string ReserveUniqueMaterialId(string baseId, int slotIndex, HashSet<string> usedIds)
    {
        string id = baseId;
        if (usedIds.Add(id))
            return id;

        id = $"{baseId}_{slotIndex}";
        int suffix = 2;
        while (!usedIds.Add(id))
        {
            id = $"{baseId}_{slotIndex}_{suffix}";
            suffix++;
        }

        return id;
    }

    private static bool HasMeaningfulName(MaterialSlot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.Name))
            return false;

        return !string.Equals(slot.Name.Trim(), $"Texture {slot.Index}", StringComparison.Ordinal);
    }

    private static string ToDescriptorSafeId(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool previousWasSeparator = false;
        foreach (char raw in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(raw))
            {
                builder.Append(raw);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        while (builder.Length > 0 && builder[^1] == '_')
            builder.Length--;

        return builder.ToString();
    }

    private static string? ToFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string fileName = Path.GetFileName(path.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }
}
