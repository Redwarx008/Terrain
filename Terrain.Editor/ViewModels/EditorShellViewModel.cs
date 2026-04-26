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
    private ModeOptionViewModel? _selectedModeOption;

    [ObservableProperty]
    private SceneViewMode _selectedSceneViewMode = SceneViewMode.Perspective;

    [ObservableProperty]
    private SceneLightingMode _selectedSceneLightingMode = SceneLightingMode.Lit;

    [ObservableProperty]
    private string _selectedToolName = "Sculpt";

    [ObservableProperty]
    private ToolOptionViewModel? _selectedTool;

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

    [ObservableProperty]
    private int _selectedMaterialSlotIndex;

    [ObservableProperty]
    private string _selectedAssetCategory = "Materials";

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private bool _isAssetBrowserVisible = true;

    [ObservableProperty]
    private bool _isSideNavVisible = true;

    [ObservableProperty]
    private bool _isInspectorVisible = true;

    public NativeStrideViewportViewModel Viewport { get; }

    public BrushParametersViewModel BrushParams { get; }

    public ClimateViewModel Climate { get; }

    public ObservableCollection<ModeOptionViewModel> Modes { get; } = new();

    public ObservableCollection<ToolOptionViewModel> Tools { get; } = new();

    public ObservableCollection<string> AssetCategories { get; } = new();

    public ObservableCollection<AssetBrowserItemViewModel> AssetItems { get; } = new();

    public ObservableCollection<ConsoleEntryViewModel> ConsoleEntries { get; } = new();

    public Array EditorModes { get; } = Enum.GetValues<EditorMode>();

    public Array SceneViewModes { get; } = Enum.GetValues<SceneViewMode>();

    public Array SceneLightingModes { get; } = Enum.GetValues<SceneLightingMode>();

    public bool IsSculptMode => SelectedMode == EditorMode.Sculpt;

    public bool IsPaintMode => SelectedMode == EditorMode.Paint;

    public bool IsFoliageMode => SelectedMode == EditorMode.Foliage;

    public bool IsWaterMode => SelectedMode == EditorMode.Water;

    public bool IsLandscapeMode => SelectedMode == EditorMode.Landscape;

    public bool IsClimateVisible => IsPaintMode || IsLandscapeMode;

    public bool IsListView => !IsGridView;

    public EditorShellViewModel()
    {
        _viewportHost = new NativeStrideViewportHost();
        Viewport = new NativeStrideViewportViewModel(_viewportHost);
        BrushParams = new BrushParametersViewModel();
        Climate = new ClimateViewModel();
        SelectedSceneViewMode = _viewportHost.SceneViewMode;

        InitializeModes();
        InitializeAssetBrowser();

        SelectedMode = _editorState.CurrentEditorMode;
        ShowMaskOverlay = _editorState.ShowMaskOverlay;
        HeatmapEnabled = _editorState.HeatmapEnabled;
        SelectedMaterialSlotIndex = _editorState.SelectedMaterialSlotIndex;
        RefreshTools();
        SyncSelectedModeOption();
        RefreshProjectState();
        RefreshHistoryState();

        _editorState.EditorModeChanged += OnEditorModeChanged;
        _editorState.HeightToolChanged += OnToolChanged;
        _editorState.PaintToolChanged += OnToolChanged;
        _editorState.OverlayChanged += OnOverlayChanged;
        _editorState.HeatmapChanged += OnHeatmapChanged;
        _editorState.MaterialSlotSelectionChanged += OnMaterialSlotSelectionChanged;
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
    private async Task ImportAssets()
    {
        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            AddConsole("Warning", "File dialog unavailable.");
            return;
        }

        var results = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Assets",
            AllowMultiple = true,
        });

        if (results.Count == 0)
        {
            return;
        }

        AddConsole("Info", $"Queued {results.Count} asset(s) for import.");
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
    private void ResetLayout()
    {
        AddConsole("Info", "Layout reset requested.");
    }

    [RelayCommand]
    private void OpenSettings()
    {
        AddConsole("Info", "Settings panel is not implemented yet.");
    }

    [RelayCommand]
    private void OpenHelp()
    {
        AddConsole("Info", "Help panel is not implemented yet.");
    }

    [RelayCommand]
    private void SetGridView()
    {
        IsGridView = true;
    }

    [RelayCommand]
    private void SetListView()
    {
        IsGridView = false;
    }

    [RelayCommand]
    private void ToggleAssetBrowser()
    {
        IsAssetBrowserVisible = !IsAssetBrowserVisible;
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
        _editorState.MaterialSlotSelectionChanged -= OnMaterialSlotSelectionChanged;
        _projectManager.DirtyChanged -= OnProjectDirtyChanged;
        _historyManager.HistoryChanged -= OnHistoryChanged;
        BrushParams.Dispose();
        Climate.Dispose();
        Viewport.Dispose();
        _viewportHost.Dispose();
    }

    partial void OnSelectedModeChanged(EditorMode value)
    {
        if (_editorState.CurrentEditorMode != value)
        {
            _editorState.CurrentEditorMode = value;
        }

        SyncSelectedModeOption();
        OnPropertyChanged(nameof(IsSculptMode));
        OnPropertyChanged(nameof(IsPaintMode));
        OnPropertyChanged(nameof(IsFoliageMode));
        OnPropertyChanged(nameof(IsWaterMode));
        OnPropertyChanged(nameof(IsLandscapeMode));
        OnPropertyChanged(nameof(IsClimateVisible));
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

        AddConsole("Info", $"Scene view mode set to {value}.");
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

    partial void OnSelectedMaterialSlotIndexChanged(int value)
    {
        if (_editorState.SelectedMaterialSlotIndex != value)
        {
            _editorState.SelectedMaterialSlotIndex = value;
        }
    }

    partial void OnSelectedModeOptionChanged(ModeOptionViewModel? value)
    {
        if (value is not null && SelectedMode != value.Mode)
        {
            SelectedMode = value.Mode;
        }
    }

    partial void OnSelectedToolChanged(ToolOptionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedToolName = value.Label;
        if (_editorState.CurrentEditorMode != value.Mode)
        {
            _editorState.CurrentEditorMode = value.Mode;
        }

        if (value.HeightTool.HasValue)
        {
            _editorState.CurrentHeightTool = value.HeightTool.Value;
            _editorState.HasSelectedTool = true;
        }
        else if (value.PaintTool.HasValue)
        {
            _editorState.CurrentPaintTool = value.PaintTool.Value;
            _editorState.HasSelectedTool = true;
        }
        else
        {
            _editorState.HasSelectedTool = true;
        }

        AddConsole("Info", $"Selected {value.Label}.");
    }

    partial void OnSelectedAssetCategoryChanged(string value)
    {
        RefreshAssetItems();
    }

    partial void OnIsGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListView));
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

    private void OnMaterialSlotSelectionChanged(object? sender, EventArgs e)
    {
        SelectedMaterialSlotIndex = _editorState.SelectedMaterialSlotIndex;
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
        Title = "Terrain Editor";
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

        SelectedTool = Tools.FirstOrDefault(static tool => tool.Label == GetDefaultToolLabel(tool.Mode))
            ?? Tools.FirstOrDefault();
        SelectedToolName = SelectedTool?.Label ?? "None";
    }

    private static ToolOptionViewModel[] CreateToolsForMode(EditorMode mode)
    {
        return mode switch
        {
            EditorMode.Sculpt =>
            [
                new("Sculpt", "Raise terrain height", "\uE74A", mode, HeightTool.Raise),
                new("Smooth", "Smooth terrain", "\uE790", mode, HeightTool.Smooth),
                new("Flatten", "Flatten terrain", "\uE81E", mode, HeightTool.Flatten),
                new("Ramp", "Create ramp", "\uE8F1", mode),
                new("Erosion", "Apply erosion", "\uE9D9", mode),
                new("Noise", "Apply terrain noise", "\uE950", mode),
            ],
            EditorMode.Paint =>
            [
                new("Paint", "Paint material", "\uE790", mode, null, PaintTool.Paint),
                new("Erase", "Erase material", "\uE74D", mode, null, PaintTool.Erase),
                new("Blend", "Blend layers", "\uE7ED", mode),
                new("Pick", "Pick material", "\uE16C", mode),
            ],
            EditorMode.Foliage =>
            [
                new("Paint Foliage", "Paint foliage", "\uE8BE", mode),
                new("Erase Foliage", "Erase foliage", "\uE74D", mode),
                new("Select", "Select foliage", "\uE14C", mode),
                new("Scatter", "Scatter foliage", "\uE8C8", mode),
            ],
            EditorMode.Water =>
            [
                new("Raise Water", "Raise water level", "\uE74A", mode),
                new("Lower Water", "Lower water level", "\uE74D", mode),
                new("Ripple", "Create water ripple", "\uE9D9", mode),
                new("Smooth Water", "Smooth water surface", "\uE790", mode),
            ],
            EditorMode.Landscape =>
            [
                new("Auto Generate", "Auto generate terrain", "\uE70F", mode),
                new("Erosion Sim", "Simulate erosion", "\uE9D9", mode),
                new("Satellite Import", "Import satellite data", "\uE8F1", mode),
                new("Climate Map", "Edit climate map", "\uE950", mode),
            ],
            _ => [],
        };
    }

    private void InitializeModes()
    {
        Modes.Add(new ModeOptionViewModel("Sculpt", "Terrain height editing", "", EditorMode.Sculpt));
        Modes.Add(new ModeOptionViewModel("Paint", "Surface material painting", "", EditorMode.Paint));
        Modes.Add(new ModeOptionViewModel("Foliage", "Vegetation placement", "\uE8BE", EditorMode.Foliage));
        Modes.Add(new ModeOptionViewModel("Water", "Water level editing", "", EditorMode.Water));
        Modes.Add(new ModeOptionViewModel("Landscape", "Landscape generation", "", EditorMode.Landscape));
    }

    private void InitializeAssetBrowser()
    {
        foreach (var category in new[] { "Materials", "Textures", "Meshes", "Brushes", "Foliage", "Prefabs" })
        {
            AssetCategories.Add(category);
        }

        RefreshAssetItems();
    }

    private void RefreshAssetItems()
    {
        AssetItems.Clear();

        foreach (var item in CreateAssetItemsForCategory(SelectedAssetCategory))
        {
            AssetItems.Add(item);
        }
    }

    private void SyncSelectedModeOption()
    {
        SelectedModeOption = Modes.FirstOrDefault(option => option.Mode == SelectedMode);
    }

    private static string GetDefaultToolLabel(EditorMode mode)
    {
        return mode switch
        {
            EditorMode.Sculpt => "Sculpt",
            EditorMode.Paint => "Paint",
            EditorMode.Foliage => "Paint Foliage",
            EditorMode.Water => "Raise Water",
            EditorMode.Landscape => "Auto Generate",
            _ => "None",
        };
    }

    private static AssetBrowserItemViewModel[] CreateAssetItemsForCategory(string category)
    {
        return category switch
        {
            "Materials" =>
            [
                new("Grass_01", category, "Material", "#9DC874", "#2E5B2A", "Grass"),
                new("Rock_01", category, "Material", "#C8CDD3", "#4E5966", "Rock"),
                new("Dirt_01", category, "Material", "#C79363", "#6A4020", "Dirt"),
                new("Sand_01", category, "Material", "#E3D0A9", "#87693B", "Sand"),
                new("Snow_01", category, "Material", "#F3F7FB", "#7E8FA3", "Snow"),
                new("Pine_Tree_01", category, "Tree", "#E5F1E3", "#355F32", "Pine"),
                new("Bush_01", category, "Foliage", "#D9ECD5", "#49713B", "Bush"),
                new("Road_Straight_01", category, "Road", "#D8D9DD", "#50545C", "Road"),
                new("Cliff_Rock_01", category, "Mesh", "#D1D5DB", "#505966", "Cliff"),
                new("Ground_Rock_01", category, "Mesh", "#D5CEC6", "#61564C", "Stone"),
                new("Mud_01", category, "Material", "#B99676", "#5D3F2C", "Mud"),
                new("Brush_Alpha_Soft", category, "Brush", "#F3F5F8", "#4B5661", "Soft"),
                new("Brush_Alpha_Hard", category, "Brush", "#ECEFF3", "#252A31", "Hard"),
                new("Add Asset", category, "Create", "#F7FBFE", "#1A9DE0", "+"),
            ],
            "Brushes" =>
            [
                new("Brush_Alpha_Soft", category, "Brush", "#F3F5F8", "#4B5661", "Soft"),
                new("Brush_Alpha_Hard", category, "Brush", "#ECEFF3", "#252A31", "Hard"),
                new("Brush_Noise_Medium", category, "Brush", "#E7EAEE", "#53606C", "Noise"),
                new("Brush_Crater_01", category, "Brush", "#EEF1F5", "#55616C", "Crater"),
                new("Brush_Ridges_02", category, "Brush", "#EEF2F5", "#5B6872", "Ridge"),
                new("Add Asset", category, "Create", "#F7FBFE", "#1A9DE0", "+"),
            ],
            "Foliage" =>
            [
                new("Pine_Tree_01", category, "Tree", "#E5F1E3", "#355F32", "Pine"),
                new("Bush_01", category, "Shrub", "#D9ECD5", "#49713B", "Bush"),
                new("Grass_Clump_A", category, "Grass", "#EEF7E6", "#4B7A3D", "Grass"),
                new("Dead_Tree_02", category, "Tree", "#EAE4D8", "#765D43", "Dead"),
                new("Forest_Rock_01", category, "Mesh", "#D1D5DB", "#55606B", "Rock"),
                new("Add Asset", category, "Create", "#F7FBFE", "#1A9DE0", "+"),
            ],
            "Textures" =>
            [
                new("Tex_Grass_01", category, "Texture", "#9DC874", "#2E5B2A", "Grass"),
                new("Tex_Rock_01", category, "Texture", "#C8CDD3", "#4E5966", "Rock"),
                new("Tex_Dirt_01", category, "Texture", "#C79363", "#6A4020", "Dirt"),
                new("Tex_Sand_01", category, "Texture", "#E3D0A9", "#87693B", "Sand"),
                new("Tex_Snow_01", category, "Texture", "#F3F7FB", "#7E8FA3", "Snow"),
                new("Add Asset", category, "Create", "#F7FBFE", "#1A9DE0", "+"),
            ],
            "Meshes" =>
            [
                new("Rock_01", category, "Mesh", "#C8CDD3", "#4E5966", "Rock"),
                new("Cliff_Block_01", category, "Mesh", "#D4D6DA", "#59616B", "Cliff"),
                new("Ground_Rock_01", category, "Mesh", "#D5CEC6", "#61564C", "Stone"),
                new("River_Stone_A", category, "Mesh", "#D8DDD8", "#5A655A", "River"),
                new("Fence_Post_01", category, "Mesh", "#D5C2A7", "#684F39", "Fence"),
                new("Add Asset", category, "Create", "#F7FBFE", "#1A9DE0", "+"),
            ],
            "Prefabs" =>
            [
                new("Camp_Small_01", category, "Prefab", "#EFE4D5", "#6A5844", "Camp"),
                new("WatchTower_01", category, "Prefab", "#E6E0D6", "#665645", "Tower"),
                new("Roadside_Signs", category, "Prefab", "#E7EDF3", "#4F6883", "Signs"),
                new("Forest_Cluster_A", category, "Prefab", "#E2F0DD", "#48703D", "Forest"),
                new("River_Bank_Set", category, "Prefab", "#DCEAF1", "#4B6775", "River"),
                new("Add Asset", category, "Create", "#F7FBFE", "#1A9DE0", "+"),
            ],
            _ =>
            [
                new("Grass_01", category, "Material", "#9DC874", "#2E5B2A", "Grass"),
                new("Rock_01", category, "Material", "#C8CDD3", "#4E5966", "Rock"),
                new("Dirt_01", category, "Material", "#C79363", "#6A4020", "Dirt"),
                new("Sand_01", category, "Material", "#E3D0A9", "#87693B", "Sand"),
                new("Snow_01", category, "Material", "#F3F7FB", "#7E8FA3", "Snow"),
                new("Mud_01", category, "Material", "#B99676", "#5D3F2C", "Mud"),
                new("Add Asset", category, "Create", "#F7FBFE", "#1A9DE0", "+"),
            ],
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
