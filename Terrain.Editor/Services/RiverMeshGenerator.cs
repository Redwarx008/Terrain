#nullable enable

using System;
using System.Linq;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

internal sealed class RiverMeshGenerator : IRiverMeshGenerator
{
    private readonly RiverRenderingService renderingService;
    private readonly RiverMeshService meshService;

    public RiverMeshGenerator(RiverRenderingService renderingService, RiverMeshService meshService)
    {
        this.renderingService = renderingService ?? throw new ArgumentNullException(nameof(renderingService));
        this.meshService = meshService ?? throw new ArgumentNullException(nameof(meshService));
    }

    public RiverGenerationResult? Generate(RiverCell[,] cells, float widthScale, float riverMinWidth, float riverMaxWidth)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var mapService = new RiverMapService(riverMinWidth, riverMaxWidth);
        mapService.Load(cells);

        var segments = mapService.ExtractSegments();
        if (segments.Count == 0)
        {
            renderingService.ClearMeshes();
            return null;
        }

        foreach (var segment in segments)
        {
            segment.TaperStart = segment.StartKind == SegmentEndKind.Source || segment.StartKind == SegmentEndKind.None;
            segment.TaperEnd = segment.EndKind == SegmentEndKind.Confluence || segment.EndKind == SegmentEndKind.Bifurcation;
        }

        meshService.BuildCenterlines(segments, cells.GetLength(0), cells.GetLength(1));
        renderingService.UpdateMeshes(segments, meshService, widthScale);

        int vertexCount = segments.Sum(static segment => (segment.Centerline?.Count ?? 0) * 2);
        int systemCount = segments.Select(static segment => segment.SystemId).Distinct().Count();
        return new RiverGenerationResult(systemCount, segments.Count, vertexCount);
    }

    public void Clear()
    {
        renderingService.ClearMeshes();
    }
}
