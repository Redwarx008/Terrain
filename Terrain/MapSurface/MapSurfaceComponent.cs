#nullable enable

using Stride.Core;
using Stride.Engine;
using Stride.Engine.Design;

namespace Terrain.MapSurface;

[DataContract("MapSurfaceComponent")]
[Display("Map Surface", Expand = ExpandRule.Once)]
[DefaultEntityComponentProcessor(typeof(MapSurfaceProcessor))]
public sealed class MapSurfaceComponent : EntityComponent
{
    [DataMember(10)]
    public Entity? TerrainEntity { get; set; }

    [DataMember(20)]
    public Entity? RiverEntity { get; set; }

    [DataMember(30)]
    public Entity? OceanEntity { get; set; }

    [DataMemberIgnore]
    internal MapSurfaceRuntimeState RuntimeState { get; } = new();
}
