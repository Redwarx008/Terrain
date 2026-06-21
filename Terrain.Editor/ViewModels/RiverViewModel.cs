#nullable enable

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Terrain.Editor.Services;
using Terrain.Rivers;

namespace Terrain.Editor.ViewModels;

public sealed partial class RiverViewModel : ObservableObject, IDisposable
{
    private readonly IRiverMapSource riverMapSource;
    private IRiverMeshGenerator? generator;

    [ObservableProperty]
    private string? _riverMapPath;

    [ObservableProperty]
    private string _statusText = "No river map loaded";

    [ObservableProperty]
    private bool _hasRiverMap;

    [ObservableProperty]
    private double _widthScale = 1.0;

    public RiverViewModel(TerrainManager terrainManager)
        : this((IRiverMapSource)terrainManager)
    {
    }

    internal RiverViewModel(IRiverMapSource riverMapSource)
    {
        this.riverMapSource = riverMapSource ?? throw new ArgumentNullException(nameof(riverMapSource));
        riverMapSource.RiverMapChanged += OnRiverMapChanged;
        SyncStateFromRiverMapSource(autoGenerate: false);
    }

    public void SetServices(RiverRenderingService renderingService, RiverMeshService meshService)
    {
        SetGenerator(new RiverMeshGenerator(renderingService, meshService));
    }

    internal void SetGenerator(IRiverMeshGenerator riverMeshGenerator)
    {
        generator = riverMeshGenerator ?? throw new ArgumentNullException(nameof(riverMeshGenerator));
        SyncStateFromRiverMapSource(autoGenerate: true);
    }

    private void OnRiverMapChanged(object? sender, EventArgs e)
    {
        SyncStateFromRiverMapSource(autoGenerate: true);
    }

    private void SyncStateFromRiverMapSource(bool autoGenerate)
    {
        var cells = riverMapSource.RiverMap;
        HasRiverMap = cells != null;
        RiverMapPath = riverMapSource.CurrentRiverMapPath;

        if (cells == null)
        {
            generator?.Clear();
            StatusText = "No river map loaded";
            return;
        }

        if (autoGenerate && TryGenerateLoadedRiverMesh(cells))
        {
            return;
        }

        StatusText = $"River map loaded: {cells.GetLength(0)}x{cells.GetLength(1)}";
    }

    private bool TryGenerateLoadedRiverMesh(RiverCell[,] cells)
    {
        if (generator == null)
            return false;

        RiverGenerationResult? result = generator.Generate(
            cells,
            (float)WidthScale,
            riverMapSource.RiverMinWidth,
            riverMapSource.RiverMaxWidth);
        if (result == null)
        {
            StatusText = "Error: No river segments found";
            return true;
        }

        StatusText = $"✓ {result.Value.SystemCount} systems, {result.Value.SegmentCount} segments, {result.Value.VertexCount} vertices";
        return true;
    }

    partial void OnWidthScaleChanged(double value)
    {
        if (riverMapSource.RiverMap is { } cells)
            TryGenerateLoadedRiverMesh(cells);
    }

    public void Dispose()
    {
        riverMapSource.RiverMapChanged -= OnRiverMapChanged;
    }
}
