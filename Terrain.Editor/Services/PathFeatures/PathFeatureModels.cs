#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;

namespace Terrain.Editor.Services.PathFeatures;

public enum PathFeatureKind
{
    Road,
    River
}

public enum PathEditorTool
{
    Road,
    River
}

public enum PathRoadStyle
{
    Dirt,
    Paved,
}

public sealed class PathFeatureStyle
{
    public float Width { get; set; } = 8.0f;
    public float Depth { get; set; } = 0.0f;
    public float SideSlope { get; set; } = 4.0f;
    public float CornerSpan { get; set; } = 0.35f;
    public PathRoadStyle RoadStyle { get; set; } = PathRoadStyle.Dirt;

    public PathFeatureStyle Clone()
    {
        return new PathFeatureStyle
        {
            Width = Width,
            Depth = Depth,
            SideSlope = SideSlope,
            CornerSpan = CornerSpan,
            RoadStyle = RoadStyle,
        };
    }
}

public sealed class PathNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Vector3 Position { get; set; }

    public PathNode Clone()
    {
        return new PathNode
        {
            Id = Id,
            Position = Position,
        };
    }
}

public sealed class PathFeature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Road";
    public PathFeatureKind Kind { get; set; } = PathFeatureKind.Road;
    public List<Guid> NodeIds { get; } = new();
    public PathFeatureStyle Style { get; set; } = new();

    public PathFeature Clone()
    {
        var clone = new PathFeature
        {
            Id = Id,
            Name = Name,
            Kind = Kind,
            Style = Style.Clone(),
        };
        clone.NodeIds.AddRange(NodeIds);
        return clone;
    }
}

public sealed class PathNetworkSnapshot
{
    public List<PathNode> Nodes { get; } = new();
    public List<PathFeature> Features { get; } = new();
    public Guid? SelectedFeatureId { get; set; }
    public Guid? SelectedNodeId { get; set; }

    public PathNetworkSnapshot Clone()
    {
        var clone = new PathNetworkSnapshot
        {
            SelectedFeatureId = SelectedFeatureId,
            SelectedNodeId = SelectedNodeId,
        };
        clone.Nodes.AddRange(Nodes.Select(static node => node.Clone()));
        clone.Features.AddRange(Features.Select(static feature => feature.Clone()));
        return clone;
    }
}

public sealed class PathFeatureSelectionChangedEventArgs : EventArgs
{
    public required Guid? SelectedFeatureId { get; init; }
    public required Guid? SelectedNodeId { get; init; }
}
