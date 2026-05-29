#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Terrain.Editor.Models;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

public sealed partial class RiverViewModel : ObservableObject, IDisposable
{
    private readonly TerrainManager terrainManager;
    private RiverRenderingService? _renderingService;
    private RiverMeshService? _meshService;

    [ObservableProperty]
    private string? _riverMapPath;

    [ObservableProperty]
    private string _statusText = "No river map loaded";

    [ObservableProperty]
    private bool _hasRiverMap;

    [ObservableProperty]
    private bool _showRivers = true;

    [ObservableProperty]
    private double _widthScale = 1.0;

    [ObservableProperty]
    private Bitmap? _previewImage;

    public RiverViewModel(TerrainManager terrainManager)
    {
        this.terrainManager = terrainManager;
        terrainManager.RiverMapChanged += OnRiverMapChanged;
    }

    public void SetServices(RiverRenderingService renderingService, RiverMeshService meshService)
    {
        _renderingService = renderingService;
        _meshService = meshService;
    }

    partial void OnShowRiversChanged(bool value)
    {
        _renderingService?.SetVisible(value);
    }

    private void OnRiverMapChanged(object? sender, EventArgs e)
    {
        HasRiverMap = terrainManager.RiverMap != null;
        RiverMapPath = terrainManager.CurrentRiverMapPath;
        StatusText = HasRiverMap
            ? $"River map loaded: {terrainManager.RiverMap!.GetLength(0)}x{terrainManager.RiverMap!.GetLength(1)}"
            : "No river map loaded";
    }

    [RelayCommand]
    public async Task ImportPng()
    {
        IStorageProvider? storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            StatusText = "Error: File dialog unavailable";
            return;
        }

        var results = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import River Map",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG Images") { Patterns = ["*.png"] }],
        });

        if (results.Count == 0) return;

        string path = results[0].TryGetLocalPath() ?? results[0].Path.ToString();
        if (string.IsNullOrEmpty(path)) return;

        terrainManager.LoadRiverMap(path);

        try
        {
            PreviewImage = new Bitmap(path);
        }
        catch
        {
            // Preview is non-critical
        }
    }

    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window
            && window.StorageProvider is { } provider)
        {
            return provider;
        }
        return null;
    }

    [RelayCommand]
    public void Generate()
    {
        var cells = terrainManager.RiverMap;
        if (cells == null)
        {
            StatusText = "Error: No river map loaded";
            return;
        }

        var mapService = new RiverMapService();
        mapService.Load(terrainManager.CurrentRiverMapPath ?? "");

        var segments = mapService.ExtractSegments();

        if (segments.Count == 0)
        {
            StatusText = "Error: No river segments found";
            return;
        }

        // Set taper flags based on segment end kinds
        foreach (var seg in segments)
        {
            seg.TaperStart = seg.StartKind == SegmentEndKind.Source || seg.StartKind == SegmentEndKind.None;
            seg.TaperEnd = seg.EndKind == SegmentEndKind.Confluence || seg.EndKind == SegmentEndKind.Bifurcation;
        }

        // Build centerlines
        _meshService?.BuildCenterlines(segments,
            cells.GetLength(0), cells.GetLength(1));

        // Generate meshes
        _renderingService?.UpdateMeshes(segments, _meshService!, (float)WidthScale);

        int vertexCount = segments.Sum(s => s.Centerline?.Count ?? 0) * 2;
        int systemCount = segments.Select(s => s.SystemId).Distinct().Count();
        StatusText = $"✓ {systemCount} systems, {segments.Count} segments, {vertexCount} vertices";
    }

    partial void OnWidthScaleChanged(double value)
    {
        Generate();
    }

    public void Dispose()
    {
        terrainManager.RiverMapChanged -= OnRiverMapChanged;
    }
}
