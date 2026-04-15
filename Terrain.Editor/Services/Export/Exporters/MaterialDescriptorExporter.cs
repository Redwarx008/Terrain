using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tommy;

namespace Terrain.Editor.Services.Export.Exporters;

/// <summary>
/// Exports material slot configuration to a standalone material_descriptor.toml file.
/// The exported file can be used by the runtime RuntimeMaterialManager without
/// depending on the editor project TOML.
/// </summary>
public class MaterialDescriptorExporter : IExporter
{
    public string Name => "Material Descriptor";
    public string FileFilter => "Material Descriptor Files (*.toml)|*.toml";
    public string DefaultExtension => "toml";

    public async Task ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct)
    {
        var slotManager = MaterialSlotManager.Instance;
        var activeSlots = slotManager.GetActiveSlots().ToList();

        if (activeSlots.Count == 0)
        {
            throw new InvalidOperationException("No material slots configured for export.");
        }

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            progress.Report(ExportProgress.Running(0, 2, "Converting material paths..."));

            string outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? "";

            var root = new TomlTable();
            var slotsArray = new TomlArray();

            foreach (var slot in activeSlots)
            {
                ct.ThrowIfCancellationRequested();

                var slotTable = new TomlTable();
                slotTable["index"] = slot.Index;

                if (!string.IsNullOrEmpty(slot.AlbedoTexturePath))
                    slotTable["albedo"] = TomlProjectConfig.MakeRelative(slot.AlbedoTexturePath, outputDir);
                else
                    slotTable["albedo"] = "";

                if (!string.IsNullOrEmpty(slot.NormalTexturePath))
                    slotTable["normal"] = TomlProjectConfig.MakeRelative(slot.NormalTexturePath, outputDir);
                else
                    slotTable["normal"] = "";

                slotsArray.Add(slotTable);
            }

            root["material_slots"] = slotsArray;

            progress.Report(ExportProgress.Running(1, 2, "Writing material descriptor file..."));

            // Ensure output directory exists
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var writer = File.CreateText(outputPath);
            root.WriteTo(writer);

            progress.Report(ExportProgress.Completed());
        }, ct);
    }
}