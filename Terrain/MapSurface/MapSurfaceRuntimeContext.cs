#nullable enable

using Stride.Core.Mathematics;
using Terrain.Resources;

namespace Terrain.MapSurface;

internal readonly record struct MapSurfaceRuntimeContext(
    TerrainRuntimeResourceBundle Resources,
    TerrainComponent Terrain,
    Vector2 MapWorldSize,
    float SeaLevel);
