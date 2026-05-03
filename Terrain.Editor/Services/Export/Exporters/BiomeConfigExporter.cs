#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Terrain.Editor.Services.Export.Exporters;

/// <summary>
/// Exports the runtime biome configuration that Terrain runtime rebuilds detail maps from.
/// The output remains a standalone TOML file for BiomeConfigPath.
/// </summary>
public class BiomeConfigExporter : IExporter
{
    /// <summary>
    /// Optional live terrain source. When present, export uses the current in-memory
    /// height scale instead of stale config snapshots.
    /// </summary>
    public TerrainManager? TerrainManager { get; set; }

    public string Name => "Biome Config";
    public string FileFilter => "Biome Config Files (*.toml)|*.toml";
    public string DefaultExtension => "toml";

    public async Task ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct)
    {
        var slotManager = MaterialSlotManager.Instance;
        var activeSlots = slotManager.GetActiveSlots().ToList();

        if (activeSlots.Count == 0)
        {
            throw new InvalidOperationException("No material slots configured for biome config export.");
        }

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            progress.Report(ExportProgress.Running(0, 2, "Collecting runtime biome config..."));

            TomlProjectConfig? currentConfig = ProjectManager.Instance.LoadConfig();
            var config = new TomlProjectConfig
            {
                Version = currentConfig?.Version ?? 2,
                Name = currentConfig?.Name ?? Path.GetFileNameWithoutExtension(outputPath),
                HeightScale = TerrainManager?.HeightScale ?? currentConfig?.HeightScale ?? 100.0f,
                MaterialSlots = activeSlots.Select(static slot => new TomlMaterialSlotConfig
                {
                    Index = slot.Index,
                    Name = slot.Name,
                    AlbedoPath = slot.AlbedoTexturePath,
                    NormalPath = slot.NormalTexturePath,
                    PropertiesPath = slot.PropertiesTexturePath,
                }).ToList(),
                Biomes = BiomeRuleService.Instance.Biomes.Select(static biome => new TomlBiomeDefinitionConfig
                {
                    Id = biome.Id,
                    Name = biome.Name,
                    DebugColorR = biome.DebugColor.X,
                    DebugColorG = biome.DebugColor.Y,
                    DebugColorB = biome.DebugColor.Z,
                    DebugColorA = biome.DebugColor.W,
                }).ToList(),
                BiomeLayers = BiomeRuleService.Instance.Layers.Select(static layer => new TomlBiomeLayerConfig
                {
                    Id = layer.Id,
                    BiomeId = layer.BiomeId,
                    Name = layer.Name,
                    Enabled = layer.Enabled,
                    Visible = layer.Visible,
                    MaterialSlotIndex = layer.MaterialSlotIndex,
                    PriorityOrder = layer.PriorityOrder,
                }).ToList(),
                BiomeModifiers = BiomeRuleService.Instance.Layers
                    .SelectMany(static layer => layer.Modifiers.Select(modifier => new TomlBiomeModifierConfig
                    {
                        Id = modifier.Id,
                        LayerId = layer.Id,
                        Name = modifier.Name,
                        Type = modifier.Type.ToString(),
                        BlendMode = modifier.BlendMode.ToString(),
                        Enabled = modifier.Enabled,
                        Visible = modifier.Visible,
                        Opacity = modifier.Opacity,
                        Min = modifier.Min,
                        Max = modifier.Max,
                        MinFalloff = modifier.MinFalloff,
                        MaxFalloff = modifier.MaxFalloff,
                        Radius = modifier.Radius,
                        AngleDegrees = modifier.AngleDegrees,
                        AngleRangeDegrees = modifier.AngleRangeDegrees,
                        Scale = modifier.Scale,
                        OffsetX = modifier.OffsetX,
                        OffsetY = modifier.OffsetY,
                        Seed = modifier.Seed,
                        Octaves = modifier.Octaves,
                        Invert = modifier.Invert,
                        TextureMaskPath = modifier.TextureMaskPath,
                        TextureMaskChannel = modifier.TextureMaskChannel,
                    }))
                    .ToList(),
            };

            progress.Report(ExportProgress.Running(1, 2, "Writing biome config file..."));
            config.WriteTo(outputPath);

            progress.Report(ExportProgress.Completed());
        }, ct);
    }
}
