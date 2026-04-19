#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Editor.UI.Panels;

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
/// Defines the available paint editing tools.
/// </summary>
public enum PaintTool
{
    Paint,
    Erase
}

public enum SceneDebugViewMode
{
    FinalOutput,
    ClimateMaskMap,
    SlopeMap,
    HeightMap
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
    private HeightTool _currentHeightTool = HeightTool.Raise;
    private PaintTool _currentPaintTool = PaintTool.Paint;
    private EditorMode _currentEditorMode = EditorMode.Sculpt;
    private bool _hasSelectedTool = false;
    private int _currentClimateId;
    private int _selectedRuleIndex = -1;
    private bool _showMaskOverlay = true;
    private SceneDebugViewMode _currentDebugViewMode = SceneDebugViewMode.FinalOutput;
    private ClimateSeason _activeSeason = ClimateSeason.All;

    /// <summary>
    /// Gets the singleton instance of EditorState.
    /// </summary>
    public static EditorState Instance => _instance.Value;

    /// <summary>
    /// 获取或设置是否有工具被选中。
    /// </summary>
    public bool HasSelectedTool
    {
        get => _hasSelectedTool;
        set
        {
            if (_hasSelectedTool != value)
            {
                _hasSelectedTool = value;
                ToolSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Tracks which high-level editing workflow owns the right-side panel.
    /// The layout switches on this value to avoid showing inactive mode tools.
    /// </summary>
    public EditorMode CurrentEditorMode
    {
        get => _currentEditorMode;
        set
        {
            if (_currentEditorMode != value)
            {
                _currentEditorMode = value;
                EditorModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Gets or sets the currently active height editing tool.
    /// Default is Raise per D-18.
    /// </summary>
    public HeightTool CurrentHeightTool
    {
        get => _currentHeightTool;
        set
        {
            if (_currentHeightTool != value)
            {
                _currentHeightTool = value;
                HeightToolChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Gets or sets the currently active paint tool.
    /// </summary>
    public PaintTool CurrentPaintTool
    {
        get => _currentPaintTool;
        set
        {
            if (_currentPaintTool != value)
            {
                _currentPaintTool = value;
                PaintToolChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Raised when the current height tool changes.
    /// </summary>
    public event EventHandler? HeightToolChanged;

    /// <summary>
    /// Raised when the current paint tool changes.
    /// </summary>
    public event EventHandler? PaintToolChanged;

    /// <summary>
    /// 当工具选择状态改变时触发（选中或取消选中）。
    /// </summary>
    public event EventHandler? ToolSelectionChanged;
    public event EventHandler? EditorModeChanged;
    public event EventHandler? ClimateSelectionChanged;
    public event EventHandler? RuleSelectionChanged;
    public event EventHandler? OverlayChanged;
    public event EventHandler? DebugViewModeChanged;
    public event EventHandler? ActiveSeasonChanged;

    /// <summary>
    /// 向后兼容属性。
    /// </summary>
    public HeightTool CurrentTool
    {
        get => _currentHeightTool;
        set => CurrentHeightTool = value;
    }

    /// <summary>
    /// 向后兼容事件。
    /// </summary>
    public event EventHandler? ToolChanged
    {
        add => HeightToolChanged += value;
        remove => HeightToolChanged -= value;
    }

    public int CurrentClimateId
    {
        get => _currentClimateId;
        set
        {
            if (_currentClimateId != value)
            {
                _currentClimateId = value;
                ClimateSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int SelectedRuleIndex
    {
        get => _selectedRuleIndex;
        set
        {
            if (_selectedRuleIndex != value)
            {
                _selectedRuleIndex = value;
                RuleSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowMaskOverlay
    {
        get => _showMaskOverlay;
        set
        {
            if (_showMaskOverlay != value)
            {
                _showMaskOverlay = value;
                OverlayChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public SceneDebugViewMode CurrentDebugViewMode
    {
        get => _currentDebugViewMode;
        set
        {
            if (_currentDebugViewMode != value)
            {
                _currentDebugViewMode = value;
                DebugViewModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// 当前活跃季节，影响规则求值中 Season 相关的过滤。
    /// </summary>
    public ClimateSeason ActiveSeason
    {
        get => _activeSeason;
        set
        {
            if (_activeSeason != value)
            {
                _activeSeason = value;
                ActiveSeasonChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private EditorState() { }

    /// <summary>
    /// Gets the preview color for the current tool.
    /// Per D-20: Preview color changes with tool.
    /// </summary>
    /// <returns>Color4 for the current tool's preview color.</returns>
    public Color4 GetToolColor() => CurrentHeightTool switch
    {
        HeightTool.Raise => new Color4(0.2f, 0.8f, 0.2f, 0.5f),    // Green
        HeightTool.Lower => new Color4(0.8f, 0.2f, 0.2f, 0.5f),   // Red
        HeightTool.Smooth => new Color4(0.2f, 0.5f, 0.8f, 0.5f),  // Blue
        HeightTool.Flatten => new Color4(0.8f, 0.8f, 0.2f, 0.5f), // Yellow
        _ => new Color4(0.5f, 0.5f, 0.5f, 0.5f)                   // Gray fallback
    };

    /// <summary>
    /// Gets the preview color for the current paint tool.
    /// </summary>
    public Color4 GetPaintToolColor() => CurrentPaintTool switch
    {
        PaintTool.Paint => new Color4(0.2f, 0.6f, 0.9f, 0.5f),   // Blue
        PaintTool.Erase => new Color4(0.9f, 0.3f, 0.3f, 0.5f), // Red
        _ => new Color4(0.5f, 0.5f, 0.5f, 0.5f)
    };
}
