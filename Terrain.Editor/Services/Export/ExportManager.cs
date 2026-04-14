using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Terrain.Editor.Services.Export;

/// <summary>
/// Manages registered exporters and provides unified export execution with error rollback.
/// </summary>
public sealed class ExportManager
{
    public static ExportManager Instance { get; } = new();

    private readonly Dictionary<string, IExporter> exporters = new();

    public IReadOnlyDictionary<string, IExporter> Exporters => exporters;

    public void Register(IExporter exporter)
    {
        exporters[exporter.Name] = exporter;
    }

    /// <summary>
    /// Execute an export by name. On failure, deletes the output file to prevent incomplete data.
    /// </summary>
    public async Task ExecuteAsync(string name, string path, IProgress<ExportProgress> progress, CancellationToken ct)
    {
        if (!exporters.TryGetValue(name, out var exporter))
            throw new InvalidOperationException($"No exporter registered with name '{name}'");

        try
        {
            await exporter.ExportAsync(path, progress, ct);
        }
        catch
        {
            // Rollback: delete incomplete file
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
            throw;
        }
    }
}