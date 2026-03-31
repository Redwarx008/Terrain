#nullable enable

using System;
using Stride.Core.Mathematics;

namespace Terrain.Editor.Services;

/// <summary>
/// Defines the available height editing tools.
/// </summary>
public enum HeightTool
{
    Raise,
    Lower,
    Smooth,
    Flatten
}

/// <summary>
/// Manages the current editor state including active tool selection.
/// Follows singleton pattern for global access.
/// Per D-18: Tool switching via toolbar only.
/// Per D-19: Current tool state stored in EditorState service.
/// </summary>
public sealed class EditorState
{
    private static readonly Lazy<EditorState> _instance = new(() => new());
    private HeightTool _currentTool = HeightTool.Raise;

    /// <summary>
    /// Gets the singleton instance of EditorState.
    /// </summary>
    public static EditorState Instance => _instance.Value;

    /// <summary>
    /// Gets or sets the currently active height editing tool.
    /// Default is Raise per D-18.
    /// </summary>
    public HeightTool CurrentTool
    {
        get => _currentTool;
        set
        {
            if (_currentTool != value)
            {
                _currentTool = value;
                ToolChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Raised when the current tool changes.
    /// </summary>
    public event EventHandler? ToolChanged;

    private EditorState() { }

    /// <summary>
    /// Gets the preview color for the current tool.
    /// Per D-20: Preview color changes with tool.
    /// </summary>
    /// <returns>Color4 for the current tool's preview color.</returns>
    public Color4 GetToolColor() => CurrentTool switch
    {
        HeightTool.Raise => new Color4(0.2f, 0.8f, 0.2f, 0.5f),    // Green
        HeightTool.Lower => new Color4(0.8f, 0.2f, 0.2f, 0.5f),   // Red
        HeightTool.Smooth => new Color4(0.2f, 0.5f, 0.8f, 0.5f),  // Blue
        HeightTool.Flatten => new Color4(0.8f, 0.8f, 0.2f, 0.5f), // Yellow
        _ => new Color4(0.5f, 0.5f, 0.5f, 0.5f)                   // Gray fallback
    };
}
