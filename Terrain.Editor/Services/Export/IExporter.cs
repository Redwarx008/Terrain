using System;
using System.Threading;
using System.Threading.Tasks;

namespace Terrain.Editor.Services.Export;

/// <summary>
/// Interface for export operations. Each export type implements this interface.
/// </summary>
public interface IExporter
{
    /// <summary>
    /// Display name shown in the Export submenu.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// File dialog filter string, e.g. "Terrain Files (*.terrain)|*.terrain".
    /// </summary>
    string FileFilter { get; }

    /// <summary>
    /// Default file extension without dot, e.g. "terrain".
    /// </summary>
    string DefaultExtension { get; }

    /// <summary>
    /// Execute the export operation.
    /// </summary>
    Task ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct);
}