#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Terrain.Editor.Services;

namespace Terrain.Editor.Services.Export.Exporters;

/// <summary>
/// Exports the current editor terrain state to a .terrain runtime file.
/// </summary>
public class TerrainExporter : IExporter
{
    private const string BakedDetailExportNotImplementedMessage =
        "Baked detail terrain export is not implemented until DetailIndex and DetailWeight payloads are provided.";

    /// <summary>
    /// The TerrainManager to export data from. Must be set before calling ExportAsync.
    /// </summary>
    public TerrainManager? TerrainManager { get; set; }

    public string Name => "Terrain";
    public string FileFilter => "Terrain Files (*.terrain)|*.terrain";
    public string DefaultExtension => "terrain";

    public Task ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromException(new InvalidOperationException(BakedDetailExportNotImplementedMessage));
    }
}
