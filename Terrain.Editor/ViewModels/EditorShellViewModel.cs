#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
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

    [ObservableProperty]
    private bool _showMaskOverlay = true;

    [ObservableProperty]
    private bool _heatmapEnabled;

    public NativeStrideViewportViewModel Viewport { get; }

    public BrushParametersViewModel BrushParams { get; }

    public ObservableCollection<ToolOptionViewModel> Tools { get; } = new();

    public ObservableCollection<ConsoleEntryViewModel> ConsoleEntries { get; } = new();

    public Array EditorModes { get; } = Enum.GetValues<EditorMode>();

    public Array SceneViewModes { get; } = Enum.GetValues<SceneViewMode>();

    public EditorShellViewModel()
    {
        _viewportHost = new NativeStrideViewportHost();
        Viewport = new NativeStrideViewportViewModel(_viewportHost);
        BrushParams = new BrushParametersViewModel();
        SelectedSceneViewMode = _viewportHost.SceneViewMode;

        SelectedMode = _editorState.CurrentEditorMode;
        ShowMaskOverlay = _editorState.ShowMaskOverlay;
        HeatmapEnabled = _editorState.HeatmapEnabled;
        RefreshTools();
        RefreshProjectState();
        RefreshHistoryState();

        _editorState.EditorModeChanged += OnEditorModeChanged;
        _editorState.HeightToolChanged += OnToolChanged;
        _editorState.PaintToolChanged += OnToolChanged;
        _editorState.OverlayChanged += OnOverlayChanged;
        _editorState.HeatmapChanged += OnHeatmapChanged;
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
    private async Task NewProject()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "New Terrain Project",
            SuggestedFileName = "terrain",
            FileTypeChoices = [new FilePickerFileType("Terrain Project") { Patterns = ["*.toml"] }],
        });

        if (result == null)
        {
            return;
        }

        string path = result.TryGetLocalPath() ?? result.Path.ToString();
        _projectManager.CreateProject(System.IO.Path.GetDirectoryName(path)!, System.IO.Path.GetFileNameWithoutExtension(path));
        RefreshProjectState();
        AddConsole("Info", $"Created project at {path}.");
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var results = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Terrain Project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Terrain Project") { Patterns = ["*.toml"] }],
        });

        if (results.Count == 0)
        {
            return;
        }

        string path = results[0].TryGetLocalPath() ?? results[0].Path.ToString();
        _projectManager.OpenProject(path);
        RefreshProjectState();
        AddConsole("Info", $"Opened project: {_projectManager.ProjectName}.");
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
    private async Task SaveProjectAs()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Project As",
            SuggestedFileName = _projectManager.ProjectName,
            FileTypeChoices = [new FilePickerFileType("Terrain Project") { Patterns = ["*.toml"] }],
        });

        if (result == null)
        {
            return;
        }

        string path = result.TryGetLocalPath() ?? result.Path.ToString();
        _projectManager.SaveProjectAs(path);
        RefreshProjectState();
        AddConsole("Info", $"Project saved to {path}.");
    }

    [RelayCommand]
    private async Task ExportTerrain()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Terrain",
            SuggestedFileName = "terrain_export",
            FileTypeChoices = [new FilePickerFileType("RAW Heightmap") { Patterns = ["*.raw"] }],
        });

        if (result == null)
        {
            return;
        }

        AddConsole("Info", $"Terrain export initiated to {result.Path}.");
    }

    [RelayCommand]
    private async Task ExportMaterialDescriptor()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Material Descriptor",
            SuggestedFileName = "materials",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });

        if (result == null)
        {
            return;
        }

        AddConsole("Info", $"Material descriptor export initiated to {result.Path}.");
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
        else
        {
            // Foliage and Climate tools don't have Height/Paint enums — mark tool as selected.
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
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public void Dispose()
    {
        _editorState.EditorModeChanged -= OnEditorModeChanged;
        _editorState.HeightToolChanged -= OnToolChanged;
        _editorState.PaintToolChanged -= OnToolChanged;
        _editorState.OverlayChanged -= OnOverlayChanged;
        _editorState.HeatmapChanged -= OnHeatmapChanged;
        _projectManager.DirtyChanged -= OnProjectDirtyChanged;
        _historyManager.HistoryChanged -= OnHistoryChanged;
        BrushParams.Dispose();
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

    partial void OnShowMaskOverlayChanged(bool value)
    {
        if (_editorState.ShowMaskOverlay != value)
        {
            _editorState.ShowMaskOverlay = value;
        }
    }

    partial void OnHeatmapEnabledChanged(bool value)
    {
        if (_editorState.HeatmapEnabled != value)
        {
            _editorState.HeatmapEnabled = value;
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

    private void OnOverlayChanged(object? sender, EventArgs e)
    {
        ShowMaskOverlay = _editorState.ShowMaskOverlay;
    }

    private void OnHeatmapChanged(object? sender, EventArgs e)
    {
        HeatmapEnabled = _editorState.HeatmapEnabled;
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

    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window
            && window.StorageProvider is { } provider)
        {
            return provider;
        }

        return null;
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