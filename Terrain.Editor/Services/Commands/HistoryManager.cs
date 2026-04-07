#nullable enable

using System;
using System.Collections.Generic;

namespace Terrain.Editor.Services.Commands;

/// <summary>
/// Manages undo/redo history with memory limits.
/// Singleton pattern for global access.
/// </summary>
public sealed class HistoryManager
{
    private static readonly Lazy<HistoryManager> _instance = new(() => new());
    public static HistoryManager Instance => _instance.Value;

    // Configuration
    private const int MaxCommandCount = 100;
    private const long MaxMemoryBytes = 500 * 1024 * 1024; // 500 MB

    // State
    private readonly List<ICommand> undoStack = new();
    private readonly List<ICommand> redoStack = new();
    private long currentMemoryUsage;
    private ICommand? activeCommand;

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;
    public int UndoCount => undoStack.Count;
    public int RedoCount => redoStack.Count;
    public long MemoryUsageBytes => currentMemoryUsage;
    public string? UndoDescription => CanUndo ? undoStack[^1].Description : null;
    public string? RedoDescription => CanRedo ? redoStack[^1].Description : null;

    /// <summary>
    /// Raised when the history state changes (undo/redo/clear).
    /// </summary>
    public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;

    private HistoryManager() { }

    /// <summary>
    /// Begins a new command. Call this when starting a stroke.
    /// The command will accumulate chunk changes during the stroke.
    /// </summary>
    public void BeginCommand(ICommand command)
    {
        if (activeCommand != null)
        {
            // Cancel any pending command
            CancelCommand();
        }

        activeCommand = command;
    }

    /// <summary>
    /// Updates the active command's affected chunks.
    /// Call this during ApplyStroke before mutating terrain data.
    /// </summary>
    public void MarkCommandChunks(int x, int z, float radius)
    {
        if (activeCommand is TerrainEditCommand terrainCommand)
        {
            terrainCommand.MarkAffectedArea(x, z, radius);
        }
    }

    /// <summary>
    /// Commits the active command. Call this when ending a stroke.
    /// </summary>
    public void CommitCommand()
    {
        if (activeCommand == null) return;

        // Terrain commands decide if they actually changed anything.
        // Empty/no-op strokes should not pollute history.
        if (activeCommand is TerrainEditCommand terrainCommand)
        {
            if (!terrainCommand.PrepareForCommit())
            {
                activeCommand = null;
                return;
            }
        }

        // Clear redo stack (new action invalidates redo history)
        ClearRedoStack();

        // Add to undo stack
        undoStack.Add(activeCommand);
        currentMemoryUsage += activeCommand.EstimatedSizeBytes;

        // Enforce memory limits
        EnforceMemoryLimits();

        activeCommand = null;

        OnHistoryChanged(HistoryChangeType.CommandAdded);
    }

    /// <summary>
    /// Cancels the active command without adding it to history.
    /// </summary>
    public void CancelCommand()
    {
        activeCommand = null;
    }

    /// <summary>
    /// Undoes the last command.
    /// </summary>
    /// <returns>True if undo was successful, false if nothing to undo.</returns>
    public bool Undo()
    {
        if (!CanUndo) return false;

        var command = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);

        command.Undo();

        redoStack.Add(command);

        OnHistoryChanged(HistoryChangeType.Undo);
        return true;
    }

    /// <summary>
    /// Redoes the last undone command.
    /// </summary>
    /// <returns>True if redo was successful, false if nothing to redo.</returns>
    public bool Redo()
    {
        if (!CanRedo) return false;

        var command = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);

        command.Execute();

        undoStack.Add(command);

        OnHistoryChanged(HistoryChangeType.Redo);
        return true;
    }

    /// <summary>
    /// Clears all history.
    /// </summary>
    public void Clear()
    {
        undoStack.Clear();
        redoStack.Clear();
        currentMemoryUsage = 0;
        activeCommand = null;

        OnHistoryChanged(HistoryChangeType.Cleared);
    }

    private void ClearRedoStack()
    {
        foreach (var command in redoStack)
        {
            currentMemoryUsage -= command.EstimatedSizeBytes;
        }
        redoStack.Clear();
    }

    private void EnforceMemoryLimits()
    {
        // Enforce command count limit
        while (undoStack.Count > MaxCommandCount)
        {
            var removed = undoStack[0];
            undoStack.RemoveAt(0);
            currentMemoryUsage -= removed.EstimatedSizeBytes;
        }

        // Enforce memory limit
        while (currentMemoryUsage > MaxMemoryBytes && undoStack.Count > 1)
        {
            var removed = undoStack[0];
            undoStack.RemoveAt(0);
            currentMemoryUsage -= removed.EstimatedSizeBytes;
        }
    }

    private void OnHistoryChanged(HistoryChangeType changeType)
    {
        HistoryChanged?.Invoke(this, new HistoryChangedEventArgs
        {
            ChangeType = changeType,
            CanUndo = CanUndo,
            CanRedo = CanRedo,
            UndoDescription = UndoDescription,
            RedoDescription = RedoDescription
        });
    }
}

/// <summary>
/// Type of history change.
/// </summary>
public enum HistoryChangeType
{
    CommandAdded,
    Undo,
    Redo,
    Cleared
}

/// <summary>
/// Event arguments for history changes.
/// </summary>
public sealed class HistoryChangedEventArgs : EventArgs
{
    public required HistoryChangeType ChangeType { get; init; }
    public required bool CanUndo { get; init; }
    public required bool CanRedo { get; init; }
    public string? UndoDescription { get; init; }
    public string? RedoDescription { get; init; }
}
