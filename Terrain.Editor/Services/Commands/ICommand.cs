#nullable enable

using Terrain.Editor.Rendering;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Base interface for all undoable commands.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Human-readable description for UI display (e.g., "Raise Terrain", "Paint Material").
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the command (applies the after state).
    /// </summary>
    void Execute();

    /// <summary>
    /// Undoes the command (restores the before state).
    /// </summary>
    void Undo();

    /// <summary>
    /// Estimated memory size in bytes for memory management.
    /// </summary>
    long EstimatedSizeBytes { get; }

    /// <summary>
    /// The data channel affected by this command (Height or MaterialIndex).
    /// </summary>
    TerrainDataChannel AffectedChannel { get; }
}
