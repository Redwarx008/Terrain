#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering.NativeViewport;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Commands;

namespace Terrain.Editor.ViewModels;

public sealed partial class EditorShellViewModel : ObservableObject, IDisposable
{
    private readonly EditorState _editorState = EditorState.Instance;
    private readonly ProjectManager _projectManager = ProjectManager.Instance;
    private readonly HistoryManager _historyManager = HistoryManager.Instance;
    private readonly NativeStrideViewportHost _viewportHost;

    [ObservableProperty]
    private string _title = "Terrain Editor";

    [ObservableProperty]
    private string _projectName = "No project";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private EditorMode _selectedMode;

    [ObservableProperty]
    private SceneViewMode _selectedSceneViewMode = SceneViewMode.Shaded;

    [ObservableProperty]
    private string _selectedToolName = "Raise";

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private string _undoLabel = "Undo";

    [ObservableProperty]
    private string _redoLabel = "Redo";

    public NativeStrideViewportViewModel Viewport { get; }

    public ObservableCollection<ToolOptionViewModel> Tools { get; } = new();

    public ObservableCollection<string> Assets { get; } = new()
    {
        "Heightmaps",
        "Material slots",
        "Splat maps",
    };

    public ObservableCollection<string> ClimateRules { get; } = new()
    {
        "Altitude bands",
        "Slope filters",
        "Moisture masks",
    };

    public ObservableCollection<ConsoleEntryViewModel> ConsoleEntries { get; } = new();

    public Array EditorModes { get; } = Enum.GetValues<EditorMode>();

    public Array SceneViewModes { get; } = Enum.GetValues<SceneViewMode>();

    public EditorShellViewModel()
    {
        _viewportHost = new NativeStrideViewportHost();
        Viewport = new NativeStrideViewportViewModel(_viewportHost);
        SelectedSceneViewMode = _viewportHost.SceneViewMode;

        SelectedMode = _editorState.CurrentEditorMode;
        RefreshTools();
        RefreshProjectState();
        RefreshHistoryState();

        _editorState.EditorModeChanged += OnEditorModeChanged;
        _editorState.HeightToolChanged += OnToolChanged;
        _editorState.PaintToolChanged += OnToolChanged;
        _projectManager.DirtyChanged += OnProjectDirtyChanged;
        _historyManager.HistoryChanged += OnHistoryChanged;

        AddConsole("Info", "Avalonia shell initialized with SimpleTheme.");
        AddConsole("Info", "Stride viewport is now hosted through a native child HWND with SDL.");
        AddConsole("Info", _viewportHost.Status);

        if (_viewportHost.HasSceneRuntime)
        {
            AddConsole("Info", "Stride SDL viewport host now owns a Scene and TerrainManager.");
        }
        else
        {
            AddConsole("Warning", _viewportHost.Status);
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        AddConsole("Info", "New project command routed. Avalonia dialog migration is pending.");
    }

    [RelayCommand]
    private void OpenProject()
    {
        AddConsole("Info", "Open project command routed. Avalonia file picker migration is pending.");
    }

    [RelayCommand]
    private void SaveProject()
    {
        if (_projectManager.IsProjectOpen)
        {
            var config = _projectManager.LoadConfig();
            if (config != null)
            {
                _projectManager.SaveConfig(config);
                AddConsole("Info", $"Saved project '{_projectManager.ProjectName}'.");
                RefreshProjectState();
                return;
            }
        }

        AddConsole("Warning", "Save requested without an open project.");
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
        AddConsole("Info", "Save As command routed. Avalonia file picker migration is pending.");
    }

    [RelayCommand]
    private void ExportTerrain()
    {
        AddConsole("Info", "Terrain export command routed. Export dialog migration is pending.");
    }

    [RelayCommand]
    private void ExportMaterialDescriptor()
    {
        AddConsole("Info", "Material descriptor export command routed. Export dialog migration is pending.");
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_historyManager.Undo())
        {
            AddConsole("Info", "Undo applied.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_historyManager.Redo())
        {
            AddConsole("Info", "Redo applied.");
        }
    }

    [RelayCommand]
    private void SelectMode(string modeName)
    {
        if (Enum.TryParse<EditorMode>(modeName, out var mode))
        {
            _editorState.CurrentEditorMode = mode;
        }
    }

    [RelayCommand]
    private void SelectSceneViewMode(SceneViewMode mode)
    {
        SelectedSceneViewMode = mode;
        AddConsole("Info", $"Scene view mode set to {mode}.");
    }

    [RelayCommand]
    private void SelectTool(ToolOptionViewModel tool)
    {
        SelectedToolName = tool.Label;
        _editorState.CurrentEditorMode = tool.Mode;

        if (tool.HeightTool.HasValue)
        {
            _editorState.CurrentHeightTool = tool.HeightTool.Value;
            _editorState.HasSelectedTool = true;
        }
        else if (tool.PaintTool.HasValue)
        {
            _editorState.CurrentPaintTool = tool.PaintTool.Value;
            _editorState.HasSelectedTool = true;
        }

        AddConsole("Info", $"Selected {tool.Label}.");
    }

    [RelayCommand]
    private void ResetLayout()
    {
        AddConsole("Info", "Layout reset requested.");
    }

    [RelayCommand]
    private void Exit()
    {
        Environment.Exit(0);
    }

    public void Dispose()
    {
        _editorState.EditorModeChanged -= OnEditorModeChanged;
        _editorState.HeightToolChanged -= OnToolChanged;
        _editorState.PaintToolChanged -= OnToolChanged;
        _projectManager.DirtyChanged -= OnProjectDirtyChanged;
        _historyManager.HistoryChanged -= OnHistoryChanged;
        Viewport.Dispose();
        _viewportHost.Dispose();
    }

    partial void OnSelectedModeChanged(EditorMode value)
    {
        if (_editorState.CurrentEditorMode != value)
        {
            _editorState.CurrentEditorMode = value;
        }
    }

    partial void OnCanUndoChanged(bool value)
    {
        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanRedoChanged(bool value)
    {
        RedoCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSceneViewModeChanged(SceneViewMode value)
    {
        if (_viewportHost.SceneViewMode != value)
        {
            _viewportHost.SetSceneViewMode(value);
        }
    }

    private void OnEditorModeChanged(object? sender, EventArgs e)
    {
        SelectedMode = _editorState.CurrentEditorMode;
        RefreshTools();
    }

    private void OnToolChanged(object? sender, EventArgs e)
    {
        SelectedToolName = SelectedMode == EditorMode.Paint
            ? _editorState.CurrentPaintTool.ToString()
            : _editorState.CurrentHeightTool.ToString();
    }

    private void OnProjectDirtyChanged(object? sender, EventArgs e)
    {
        RefreshProjectState();
    }

    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs e)
    {
        RefreshHistoryState();
    }

    private void RefreshProjectState()
    {
        IsDirty = _projectManager.IsDirty;
        ProjectName = _projectManager.IsProjectOpen ? _projectManager.ProjectName : "No project";
        Title = IsDirty ? $"Terrain Editor - {ProjectName} *" : $"Terrain Editor - {ProjectName}";
    }

    private void RefreshHistoryState()
    {
        CanUndo = _historyManager.CanUndo;
        CanRedo = _historyManager.CanRedo;
        UndoLabel = _historyManager.UndoDescription is { Length: > 0 } undo ? $"Undo {undo}" : "Undo";
        RedoLabel = _historyManager.RedoDescription is { Length: > 0 } redo ? $"Redo {redo}" : "Redo";
    }

    private void RefreshTools()
    {
        Tools.Clear();
        foreach (var tool in CreateToolsForMode(SelectedMode))
        {
            Tools.Add(tool);
        }

        SelectedToolName = Tools.FirstOrDefault()?.Label ?? "None";
    }

    private static ToolOptionViewModel[] CreateToolsForMode(EditorMode mode)
    {
        return mode switch
        {
            EditorMode.Sculpt =>
            [
                new("Raise", "Raise terrain height", mode, HeightTool.Raise),
                new("Lower", "Lower terrain height", mode, HeightTool.Lower),
                new("Smooth", "Smooth terrain", mode, HeightTool.Smooth),
                new("Flatten", "Flatten terrain to target height", mode, HeightTool.Flatten),
            ],
            EditorMode.Paint =>
            [
                new("Paint", "Paint material onto terrain", mode, null, PaintTool.Paint),
                new("Erase", "Erase material from terrain", mode, null, PaintTool.Erase),
            ],
            EditorMode.Foliage =>
            [
                new("Place", "Place foliage instances", mode),
                new("Remove", "Remove foliage instances", mode),
            ],
            EditorMode.Climate =>
            [
                new("Select Rule", "Select climate rule", mode),
                new("Paint Mask", "Paint climate mask", mode),
            ],
            _ => [],
        };
    }

    private void AddConsole(string level, string message)
    {
        ConsoleEntries.Add(new ConsoleEntryViewModel(level, message));
        while (ConsoleEntries.Count > 200)
        {
            ConsoleEntries.RemoveAt(0);
        }
    }
}
